"""Create an isolated Unity validation project without modifying this package."""
import argparse
import json
import shutil
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("project", type=Path)
parser.add_argument("--modules", choices=["full", "core", "agent", "assistant", "mcp"], default="full")
parser.add_argument("--samples", action="store_true")
args = parser.parse_args()
repo = Path(__file__).resolve().parents[1]
project = args.project.resolve()
if project.exists() and any(project.iterdir()):
    parser.error("Choose a new or empty validation project directory.")
keep = {
    "full": {"Runtime", "Agent", "Editor", "EditorAssistant", "MCP"},
    "core": {"Runtime", "Editor"},
    "agent": {"Runtime", "Agent", "Editor"},
    "assistant": {"Runtime", "Agent", "Editor", "EditorAssistant"},
    "mcp": {"Runtime", "Agent", "Editor", "MCP"},
}[args.modules]
package = project / "Packages/com.unai.universal-ai-connector"
package.mkdir(parents=True)

def ignore_modules(directory, names):
    if Path(directory) == repo / "Scripts":
        return [name for name in names if name.removesuffix(".meta") not in keep]
    return []

shutil.copytree(repo / "Scripts", package / "Scripts", ignore=ignore_modules)
for name in ["package.json", "package.json.meta", "Scripts.meta", "LICENSE", "LICENSE.meta"]:
    shutil.copy2(repo / name, package / name)
shutil.copytree(repo / "Samples~", package / "Samples~")
if args.modules == "full":
    shutil.copytree(repo / "Tests", package / "Tests")
    shutil.copy2(repo / "Tests.meta", package / "Tests.meta")
assets = project / "Assets"
(assets / "Editor").mkdir(parents=True)
shutil.copy2(repo / "Tools~/UnaiValidationBuild.cs", assets / "Editor/UnaiValidationBuild.cs")
dependencies = {
    "com.unity.nuget.newtonsoft-json": "3.2.2",
    "com.unity.test-framework": "1.6.0",
}
# Explicit built-in modules used by the SDK and its editor tools, with no UI package dependency.
for module in ["animation", "audio", "imageconversion", "imgui", "jsonserialize", "particlesystem",
               "physics", "physics2d", "screencapture", "terrain", "terrainphysics", "unitywebrequest"]:
    dependencies[f"com.unity.modules.{module}"] = "1.0.0"
if args.samples:
    shutil.copytree(repo / "Samples~/MinimalChat", assets / "MinimalChat")
    if args.modules == "full":
        dependencies["com.unity.ugui"] = "2.0.0"
        shutil.copytree(repo / "Samples~/ChatAndAgents", assets / "ChatAndAgents")
manifest = {"dependencies": dependencies}
if args.modules == "full":
    manifest["testables"] = ["com.unai.universal-ai-connector"]
(project / "Packages/manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
(project / "ProjectSettings").mkdir()
(project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 6000.0.68f1\n", encoding="utf-8")
print(f"Prepared {args.modules} validation project at {project}")
