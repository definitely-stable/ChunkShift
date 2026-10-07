#!/usr/bin/env python3
import argparse, json
from pathlib import Path

LANES=("G3-RUN","G3-FILE")

def load(path):
    return json.loads(Path(path).read_text(encoding="utf-8"))

def aggregate(split):
    families=split["families"]
    h0=sum(int(v["h0Bytes"]) for v in families.values())
    result=[]
    for lane in LANES:
        factor=sum(int(v["lanes"][lane]["factorBytes"]) for v in families.values())
        saved=h0-factor
        result.append({
            "lane":lane,
            "h0Bytes":h0,
            "factorBytes":factor,
            "savedBytes":saved,
            "reductionVsCsp":saved/h0,
            "meetsRfcSizeGate":20*saved >= 3*h0,
        })
    return result

def main():
    ap=argparse.ArgumentParser()
    ap.add_argument("--compact",required=True)
    ap.add_argument("--result",required=True)
    args=ap.parse_args()
    compact=load(args.compact)
    expected=load(args.result)
    for role in ("calibration","evaluation"):
        actual=aggregate(compact["splits"][role])
        exp=expected[role]["lanes"]
        for a,e in zip(actual,exp,strict=True):
            for key in ("lane","h0Bytes","factorBytes","savedBytes","meetsRfcSizeGate"):
                assert a[key] == e[key], (role,key,a[key],e[key])
            assert abs(a["reductionVsCsp"]-e["reductionVsCsp"]) < 1e-15
    assert expected["status"] == "REJECT"
    assert expected["runtimeEligibleLanes"] == []
    assert expected["nextAction"] == "RUN_G4_EXECUTABLE_NORMALIZATION"
    print(json.dumps({r:aggregate(compact["splits"][r]) for r in ("calibration","evaluation")},sort_keys=True,separators=(",",":")))

if __name__=="__main__":
    main()
