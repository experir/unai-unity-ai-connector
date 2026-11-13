"""Regenerate the scenes of the InGameChat, NpcDialogue and EditorAssistantPlayground samples.

Prepares a temporary Unity project, imports the samples, runs UnaiSampleSceneBuilder in batch mode,
then copies the generated scenes, sprites and .meta files back into Samples~.

    python Tools~/build_sample_scenes.py --unity "C:/Program Files/Unity/Hub/Editor/6000.0.68f1/Editor/Unity.exe"
"""
import argparse
import json
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

SAMPLES = ["InGameChat", "NpcDialogue", "EditorAssistantPlayground"]

parser = argparse.ArgumentParser()
parser.add_argument("--unity", required=True, type=Path, help="Path to Unity.exe (use the oldest supported version)")
parser.add_argument("--project", type=Path, help="Temporary project directory (default: new temp dir)")
parser.add_argument("--keep", action="store_true", help="Keep the temporary project")
parser.add_argument("--ugui", default="2.0.0", help="com.unity.ugui version matching the editor (6000.0: 2.0.0)")
args = parser.parse_args()

repo = Path(__file__).resolve().parents[1]
project = (args.project or Path(tempfile.mkdtemp(prefix="unai-samples-")) / "project").resolve()
subprocess.run([sys.executable, "-I", str(repo / "Tools~/prepare_unity_project.py"), str(project), "--samples"], check=True)

manifest_path = project / "Packages/manifest.json"
manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
manifest["dependencies"]["com.unity.ugui"] = args.ugui
manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")

for name in SAMPLES:
    target = project / "Assets" / name
    shutil.copytree(repo / "Samples~" / name, target, ignore=shutil.ignore_patterns("*.unity", "*.unity.meta", "Art", "Art.meta"))
shutil.copy2(repo / "Tools~/UnaiSampleSceneBuilder.cs", project / "Assets/Editor/UnaiSampleSceneBuilder.cs")


def run_unity(method: str, log: str, *extra: str) -> None:
    log_path = project / log
    command = [str(args.unity), "-batchmode", "-nographics", "-projectPath", str(project), "-quit", "-logFile", str(log_path)]
    if method:
        command += ["-executeMethod", method]
    result = subprocess.run(command + list(extra))
    text = log_path.read_text(encoding="utf-8", errors="replace")
    errors = [line for line in text.splitlines() if "error CS" in line or "Exception:" in line]
    if result.returncode != 0 or errors:
        print("\n".join(errors[:40]) or text[-4000:])
        sys.exit(f"Unity failed running {method} (exit {result.returncode}); log: {log_path}")


# Open once to resolve packages, then import TMP Essential Resources synchronously (-importPackage).
run_unity("", "open.log")
essentials = next((project / "Library/PackageCache").glob("com.unity.ugui@*/Package Resources/TMP Essential Resources.unitypackage"))
run_unity("", "import-tmp.log", "-importPackage", str(essentials))
run_unity("UnaiSampleSceneBuilder.BuildAll", "build-scenes.log")

for name in SAMPLES:
    source, target = project / "Assets" / name, repo / "Samples~" / name
    for path in source.rglob("*"):
        relative = path.relative_to(source)
        if path.is_dir():
            continue
        # Scenes and generated art are copied whole; for authored files only their .meta is new.
        if path.suffix == ".meta" or path.suffix == ".unity" or relative.parts[0] == "Art":
            (target / relative).parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(path, target / relative)
    folder_meta = project / "Assets" / f"{name}.meta"
    shutil.copy2(folder_meta, repo / "Samples~" / f"{name}.meta")
    print(f"Updated Samples~/{name}")

if not args.keep:
    shutil.rmtree(project.parent if args.project is None else project, ignore_errors=True)
else:
    print(f"Kept project at {project}")
