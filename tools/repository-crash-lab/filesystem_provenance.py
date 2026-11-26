"""Handle-bound filesystem attribution for trusted, test-owned hosted fixtures.

Not a filesystem allowlist, remote detection proof, or durability contract.
Linux mount IDs and Windows volume identifiers are scoped to the observation.
"""
import ctypes
import os
from pathlib import Path
import re


def require(ok, message):
    if not ok:
        raise ValueError(message)


def unescape_mount_field(value):
    # Linux mountinfo escapes whitespace and backslash; decode exactly once.
    return re.sub(r"\\(040|011|012|134)", lambda m: chr(int(m[1], 8)), value)


def linux_mount(fdinfo, mountinfo, device):
    ids = [line.split(":", 1)[1].strip() for line in fdinfo.splitlines()
           if line.startswith("mnt_id:")]
    require(len(ids) == 1 and ids[0].isascii() and ids[0].isdecimal(), "missing or ambiguous fd mount ID")
    matches = [line for line in mountinfo.splitlines() if line.split(" ", 1)[0] == ids[0]]
    require(len(matches) == 1, "missing or ambiguous mountinfo ID")
    left, sep, right = matches[0].partition(" - ")
    fields, tail = left.split(), right.split()
    require(sep and len(fields) >= 6 and len(tail) == 3, "invalid mountinfo entry")
    require(fields[2] == device, "mountinfo device differs from fstat")
    return dict(method="linux-fdinfo-mountinfo", mount_id=int(ids[0]), parent_mount_id=int(fields[1]),
                device=device, root=unescape_mount_field(fields[3]),
                mount_point=unescape_mount_field(fields[4]), mount_options=fields[5],
                optional_fields=fields[6:], filesystem=tail[0],
                mount_source=unescape_mount_field(tail[1]), super_options=tail[2],
                storage_class="UNQUALIFIED")


def linux_handle(fd):
    info = os.fstat(fd)
    device = f"{os.major(info.st_dev)}:{os.minor(info.st_dev)}"
    observed = linux_mount(Path(f"/proc/self/fdinfo/{fd}").read_text(encoding="utf-8"),
                           Path("/proc/self/mountinfo").read_text(encoding="utf-8"), device)
    observed["object_id"] = str(info.st_ino)
    return observed


def windows_volume_guid(final_path):
    match = re.match(r"^(\\\\\?\\Volume\{[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\}\\)", final_path)
    require(match is not None, "no volume GUID in handle path; no drive-root fallback")
    return match[1].lower()


class WindowsProvenance:
    def __init__(self, kernel):
        from ctypes import wintypes
        self.volume_info = kernel.GetVolumeInformationByHandleW
        self.volume_info.argtypes = [wintypes.HANDLE, wintypes.LPWSTR, wintypes.DWORD,
                                     ctypes.POINTER(wintypes.DWORD), ctypes.POINTER(wintypes.DWORD),
                                     ctypes.POINTER(wintypes.DWORD), wintypes.LPWSTR, wintypes.DWORD]
        self.volume_info.restype = wintypes.BOOL
        self.final_path = kernel.GetFinalPathNameByHandleW
        self.final_path.argtypes = [wintypes.HANDLE, wintypes.LPWSTR, wintypes.DWORD, wintypes.DWORD]
        self.final_path.restype = wintypes.DWORD
        self.file_info = kernel.GetFileInformationByHandleEx
        self.file_info.argtypes = [wintypes.HANDLE, ctypes.c_int, ctypes.c_void_p, wintypes.DWORD]
        self.file_info.restype = wintypes.BOOL
        self.drive_type = kernel.GetDriveTypeW
        self.drive_type.argtypes = [wintypes.LPCWSTR]
        self.drive_type.restype = wintypes.UINT

        class FileIdInfo(ctypes.Structure):
            _fields_ = [("volume_serial", ctypes.c_uint64), ("file_id", ctypes.c_ubyte * 16)]

        self.id_type = FileIdInfo

    def observe(self, handle):
        from ctypes import wintypes
        fs = ctypes.create_unicode_buffer(256)
        serial, maximum, flags = wintypes.DWORD(), wintypes.DWORD(), wintypes.DWORD()
        if not self.volume_info(handle, None, 0, ctypes.byref(serial), ctypes.byref(maximum),
                                ctypes.byref(flags), fs, len(fs)):
            raise ctypes.WinError(ctypes.get_last_error())
        path = ctypes.create_unicode_buffer(32768)
        count = self.final_path(handle, path, len(path), 1)  # VOLUME_NAME_GUID | FILE_NAME_NORMALIZED
        if count == 0:
            raise ctypes.WinError(ctypes.get_last_error())
        require(count < len(path), "handle path exceeds bounded buffer")
        guid = windows_volume_guid(path.value)
        info = self.id_type()
        if not self.file_info(handle, 18, ctypes.byref(info), ctypes.sizeof(info)):  # FileIdInfo
            raise ctypes.WinError(ctypes.get_last_error())
        require(bool(fs.value), "empty filesystem name")
        return dict(method="windows-handle-volume", filesystem=fs.value, volume_guid=guid,
                    volume_serial=f"{serial.value:08x}", file_id_volume_serial=f"{info.volume_serial:016x}",
                    object_id=bytes(info.file_id).hex(), filesystem_flags=f"{flags.value:08x}",
                    maximum_component_length=maximum.value, drive_type=self.drive_type(guid),
                    mount_options=None, storage_class="UNQUALIFIED")


def volume_key(observed):
    if observed["method"] == "linux-fdinfo-mountinfo":
        return (observed["method"], observed["mount_id"], observed["device"], observed["filesystem"])
    require(observed["method"] == "windows-handle-volume", "unknown provenance method")
    return (observed["method"], observed["volume_guid"], observed["volume_serial"],
            observed["file_id_volume_serial"], observed["filesystem"])
