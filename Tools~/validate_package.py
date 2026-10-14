"""Validate package metadata and repository documentation using only Python's standard library."""
import json
import re
from pathlib import Path

repo = Path(__file__).resolve().parents[1]
package = json.loads((repo / "package.json").read_text(encoding="utf-8-sig"))
version = re.search(r'public const string Current = "([^"]+)"',
                    (repo / "Scripts/Runtime/UnaiVersion.cs").read_text(encoding="utf-8-sig")).group(1)
assert version == package["version"], "Runtime/package version mismatch"
assert package["dependencies"]["com.unity.nuget.newtonsoft-json"] == "3.2.2"
for sample in package["samples"]:
    assert (repo / sample["path"]).is_dir(), sample["path"]

def anchors(path):
    result, code = set(), False
    for line in path.read_text(encoding="utf-8-sig").splitlines():
        if line.startswith("```"):
            code = not code
        if not code and re.match(r"^#{1,6} ", line):
            title = re.sub(r"^#{1,6} ", "", line).lower()
            result.add(re.sub(r"[^\w\- ]", "", title).replace(" ", "-"))
    return result

checked = 0
for path in list(repo.glob("*.md")) + list((repo / "Docs").rglob("*.md")) + list((repo / "Samples~").rglob("*.md")):
    content = path.read_text(encoding="utf-8-sig")
    assert content.count("```") % 2 == 0, path
    assert not re.search(r"https?://(?:www\.)?experir\.com", content), f"Retired website link: {path}"
    for target in re.findall(r"\]\(([^)]+)\)", content):
        if re.match(r"(?:https?://|mailto:)", target):
            continue
        local, _, fragment = target.partition("#")
        resolved = (path.parent / local).resolve() if local else path
        assert resolved.exists(), f"{path}: missing {target}"
        if fragment and resolved.suffix == ".md":
            assert fragment in anchors(resolved), f"{path}: missing {target}"
        checked += 1
for root in [repo / "Scripts", repo / "Tests", repo / "Docs"]:
    for path in root.rglob("*"):
        if path.suffix != ".meta":
            assert Path(str(path) + ".meta").is_file(), f"Missing Unity metadata: {path}"
print(f"Package/version, samples, Unity metadata and {checked} local documentation links passed.")
