// Minimal PATCH-GAP-001 adapter around the pinned upstream Puffin core.
//
// This binary intentionally exposes only the puffhuff path required by the
// Stage-A G5 structural locator. It does not contain puffdiff, puffpatch,
// bsdiff, Zucchini, Brillo, or the Android build system.

#include <algorithm>
#include <cstdint>
#include <fstream>
#include <iostream>
#include <memory>
#include <string>
#include <utility>
#include <vector>

#include "puffin/file_stream.h"
#include "puffin/memory_stream.h"
#include "puffin/src/include/puffin/common.h"
#include "puffin/src/include/puffin/huffer.h"
#include "puffin/src/include/puffin/puffer.h"
#include "puffin/src/include/puffin/utils.h"
#include "puffin/src/puffin_stream.h"

namespace {

using puffin::BitExtent;
using puffin::Buffer;
using puffin::ByteExtent;
using puffin::FileStream;
using puffin::Huffer;
using puffin::MemoryStream;
using puffin::Puffer;
using puffin::PuffinStream;
using std::string;
using std::vector;

struct Options {
  string operation;
  string src_file;
  string dst_file;
  string src_file_type;
  bool verbose = false;
};

int Fail(const string& message) {
  std::cerr << "puffin-locator: " << message << "\n";
  return 2;
}

void PrintHelp() {
  std::cout
      << "PATCH-GAP-001 pinned Puffin locator\n"
      << "Supported operation: puffhuff\n"
      << "Required: --src_file=PATH --dst_file=PATH "
         "--src_file_type=deflate|gzip|zlib|zip\n"
      << "Optional: --verbose\n";
}

bool ParseOptions(int argc, char** argv, Options* options) {
  for (int i = 1; i < argc; ++i) {
    const string arg(argv[i]);
    if (arg == "--help" || arg == "-h") {
      PrintHelp();
      return false;
    }
    if (arg == "--verbose") {
      options->verbose = true;
      continue;
    }

    const auto split = arg.find('=');
    if (split == string::npos || split == 0 || arg.rfind("--", 0) != 0) {
      throw std::runtime_error("unsupported argument: " + arg);
    }

    const string name = arg.substr(2, split - 2);
    const string value = arg.substr(split + 1);
    if (name == "operation") {
      options->operation = value;
    } else if (name == "src_file") {
      options->src_file = value;
    } else if (name == "dst_file") {
      options->dst_file = value;
    } else if (name == "src_file_type") {
      options->src_file_type = value;
    } else {
      throw std::runtime_error("unsupported argument: --" + name);
    }
  }
  return true;
}

bool ReadFile(const string& path, Buffer* data) {
  std::ifstream stream(path, std::ios::binary | std::ios::ate);
  if (!stream) {
    return false;
  }

  const auto end = stream.tellg();
  if (end < 0) {
    return false;
  }
  const auto size = static_cast<uint64_t>(end);
  if (size > static_cast<uint64_t>(std::numeric_limits<size_t>::max())) {
    return false;
  }

  data->resize(static_cast<size_t>(size));
  stream.seekg(0, std::ios::beg);
  if (!data->empty()) {
    stream.read(reinterpret_cast<char*>(data->data()),
                static_cast<std::streamsize>(data->size()));
  }
  return stream.good() || stream.eof();
}

bool LocateDeflates(const Buffer& data,
                    const string& file_type,
                    vector<BitExtent>* deflates) {
  if (file_type == "deflate") {
    return puffin::LocateDeflatesInDeflateStream(
        data.data(), data.size(), 0, deflates, nullptr);
  }
  if (file_type == "zlib") {
    return puffin::LocateDeflatesInZlib(data, deflates);
  }
  if (file_type == "gzip") {
    return puffin::LocateDeflatesInGzip(data, deflates);
  }
  if (file_type == "zip") {
    return puffin::LocateDeflatesInZipArchive(data, deflates);
  }
  return false;
}

bool RunPuffHuff(const Options& options, vector<BitExtent>* deflates) {
  Buffer input;
  if (!ReadFile(options.src_file, &input)) {
    return false;
  }
  if (!LocateDeflates(input, options.src_file_type, deflates)) {
    return false;
  }
  if (deflates->empty()) {
    return false;
  }

  auto src_stream = FileStream::Open(options.src_file, true, false);
  if (!src_stream) {
    return false;
  }

  vector<ByteExtent> puffs;
  uint64_t puff_size = 0;
  if (!puffin::FindPuffLocations(src_stream, *deflates, &puffs, &puff_size)) {
    return false;
  }

  auto puffer = std::make_shared<Puffer>();
  auto puff_reader = PuffinStream::CreateForPuff(
      std::move(src_stream), puffer, puff_size, *deflates, puffs);
  if (!puff_reader) {
    return false;
  }

  Buffer puff_buffer;
  auto puff_writer = MemoryStream::CreateForWrite(&puff_buffer);
  if (!puff_writer) {
    return false;
  }

  Buffer transfer(1024 * 1024);
  uint64_t written = 0;
  while (written < puff_size) {
    const auto count = std::min<uint64_t>(transfer.size(), puff_size - written);
    if (!puff_reader->Read(transfer.data(), count) ||
        !puff_writer->Write(transfer.data(), count)) {
      return false;
    }
    written += count;
  }

  auto dst_stream = FileStream::Open(options.dst_file, false, true);
  if (!dst_stream) {
    return false;
  }
  auto puff_memory = MemoryStream::CreateForRead(puff_buffer);
  if (!puff_memory) {
    return false;
  }

  auto huffer = std::make_shared<Huffer>();
  auto huff_writer = PuffinStream::CreateForHuff(
      std::move(dst_stream), huffer, puff_size, *deflates, puffs);
  if (!huff_writer) {
    return false;
  }

  uint64_t read = 0;
  while (read < puff_size) {
    const auto count = std::min<uint64_t>(transfer.size(), puff_size - read);
    if (!puff_memory->Read(transfer.data(), count) ||
        !huff_writer->Write(transfer.data(), count)) {
      return false;
    }
    read += count;
  }

  return huff_writer->Close();
}

}  // namespace

int main(int argc, char** argv) {
  Options options;
  try {
    if (!ParseOptions(argc, argv, &options)) {
      return 0;
    }
  } catch (const std::exception& ex) {
    return Fail(ex.what());
  }

  if (options.operation != "puffhuff") {
    return Fail("only --operation=puffhuff is supported");
  }
  if (options.src_file.empty() || options.dst_file.empty() ||
      options.src_file_type.empty()) {
    return Fail("--src_file, --dst_file and --src_file_type are required");
  }

  vector<BitExtent> deflates;
  if (!RunPuffHuff(options, &deflates)) {
    return Fail("Puffin puffhuff failed");
  }

  if (options.verbose) {
    std::cerr << "src_deflates_bit: " << puffin::ExtentsToString(deflates)
              << "\n";
  }
  return 0;
}
