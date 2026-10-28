"""Report per-test timing from the TRX files produced by the existing test gate."""
from pathlib import Path
import os
import xml.etree.ElementTree as ET


def seconds(value):
    hours, minutes, remainder = value.split(":")
    return int(hours) * 3600 + int(minutes) * 60 + float(remainder)


def report(root):
    records = []
    for path in root.glob("Tests/**/TestResults/*.trx"):
        for result in ET.parse(path).getroot().iter():
            if result.tag.rsplit("}", 1)[-1] != "UnitTestResult":
                continue
            duration = result.get("duration")
            if duration:
                records.append((seconds(duration), result.get("testName", "unknown")))
    if not records:
        return "No completed TRX test timings were available; inspect the test step for failures.\n"
    lines = ["## Slowest test cases", "", "| Seconds | Test |", "| ---: | --- |"]
    for duration, name in sorted(records, reverse=True)[:20]:
        safe_name = name.replace("|", "\\|").replace("\n", " ").replace("<", "&lt;")
        lines.append(f"| {duration:.3f} | {safe_name} |")
    lines.extend(["", f"Measured {len(records)} completed test cases. No test filters were applied.", ""])
    return "\n".join(lines)


if __name__ == "__main__":
    output = report(Path(__file__).resolve().parents[2])
    print(output)
    if summary := os.environ.get("GITHUB_STEP_SUMMARY"):
        with open(summary, "a", encoding="utf-8") as stream:
            stream.write(output)
