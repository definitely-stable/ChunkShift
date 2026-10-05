#!/usr/bin/env python3
import argparse, json
from pathlib import Path

LANES=("G1-B1-R64","G1-B4-R256","G1-B8-R512","G1-B32-R2048")

def load(path):
    return json.loads(Path(path).read_text(encoding="utf-8"))

def recompute(compact):
    family=compact["families"]
    h0=sum(int(v["h0Bytes"]) for v in family.values())
    lanes=[]
    previous=h0
    for lane in LANES:
        factor=sum(int(v["lanes"][lane]) for v in family.values())
        assert factor <= previous, (lane,factor,previous)
        saved=h0-factor
        lanes.append({
            "lane":lane,"h0Bytes":h0,"factorBytes":factor,"savedBytes":saved,
            "reductionVsCsp":saved/h0,
            "meetsRfcSizeGate":20*saved >= 3*h0,
        })
        previous=factor
    b32_saved=sum(
        int(f["h0PatchBytes"])-int(f["lanePatchBytes"]["G1-B32-R2048"])
        for f in compact["improvedFilesB32"]
    )
    assert b32_saved == lanes[-1]["savedBytes"]
    improved=len(compact["improvedFilesB32"])
    assert improved + int(compact["unchangedFileCountB32"]) == int(compact["sampleFileCount"])
    amplification=int(compact["factorBaseBytesRead"])/int(compact["h0BaseBytesRead"])
    return {"lanes":lanes,"improvedFileCountB32":improved,"baseReadAmplification":amplification}

def main():
    ap=argparse.ArgumentParser()
    ap.add_argument("--compact",required=True)
    ap.add_argument("--result",required=True)
    args=ap.parse_args()
    actual=recompute(load(args.compact))
    expected=load(args.result)
    assert actual["lanes"] == expected["lanes"], (actual["lanes"],expected["lanes"])
    assert actual["improvedFileCountB32"] == expected["improvedFileCountB32"]
    assert actual["baseReadAmplification"] == expected["baseReadAmplification"]
    assert expected["status"] == "CALIBRATION_ONLY"
    assert expected["earlyRuntimeEligibleLanes"] == []
    assert expected["nextAction"] == "RUN_FROZEN_EVALUATION_ALL_G1_LANES"
    print(json.dumps(actual,sort_keys=True,separators=(",",":")))

if __name__=="__main__":
    main()
