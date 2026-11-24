"""Publish the Windows package built for this main-branch commit."""

import hashlib
import json
import os
import subprocess
import tempfile
from pathlib import Path


def run(*args, input_text=None):
    result = subprocess.run(args, input=input_text, stdout=subprocess.PIPE,
                            check=True, encoding="utf-8")
    return result.stdout.strip()


def sha256(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def verify_download(tag, package, expected_digest):
    with tempfile.TemporaryDirectory() as directory:
        run("gh", "release", "download", tag, "--pattern", package, "--dir", directory)
        if sha256(Path(directory) / package) != expected_digest:
            raise ValueError(f"SHA-256 mismatch for {package}")


def publish():
    version = os.environ["VERSION"]
    commit = os.environ["GITHUB_SHA"]
    endpoint = f"repos/{os.environ['GH_REPO']}/releases"
    tag = f"v{version}"
    package = f"Biandan-Windows-x64-{version}.zip"
    archive = Path("release") / package

    release_id = run("gh", "api", "--paginate", endpoint + "?per_page=100",
                     "--jq", f'.[] | select(.tag_name == "{tag}") | .id')
    tag_commit = None
    if run("git", "tag", "--list", tag):
        tag_commit = run("git", "rev-list", "-1", f"refs/tags/{tag}")

    if release_id:
        info = json.loads(run("gh", "api", f"{endpoint}/{release_id}"))
        if not info["draft"]:
            if not tag_commit:
                raise ValueError(f"Missing source tag for published release {tag}")
            run("git", "merge-base", "--is-ancestor", tag_commit, commit)
            asset = next((a for a in info["assets"] if a["name"] == package
                          and a["state"] == "uploaded"), None)
            if not asset or not (asset.get("digest") or "").startswith("sha256:"):
                raise ValueError(f"Missing verified asset on published release {tag}")
            verify_download(tag, package, asset["digest"].removeprefix("sha256:"))
            print(f"Verified existing release {tag}")
            return
        if info["target_commitish"] != commit:
            raise ValueError(f"The existing draft for {tag} targets a different commit")

    if tag_commit and tag_commit != commit:
        raise ValueError(f"{tag} already points to {tag_commit}")

    digest = sha256(archive)
    previous = run("git", "tag", "--merged", commit, "--sort=-version:refname",
                   "--list", "v[0-9]*").splitlines()
    previous_tag = next((name for name in previous if name != tag), None)
    history = [f"{previous_tag}..{commit}"] if previous_tag else ["-1", commit]
    changes = run("git", "log", "--no-merges", "--format=- %s", *history)
    notes = (f"## 本版更新\n\n{changes}\n\n## 下载与运行\n\n"
             f"适用于 Windows 10/11 x64 和 .NET Framework 4.8 或更高版本。"
             f"先退出托盘中的旧版便单，完整解压 `{package}` 后运行 `Biandan.exe`。\n\n"
             f"SHA-256 为 `{digest}`。\n")

    if not release_id:
        run("gh", "release", "create", tag, "--draft", "--target", commit,
            "--title", f"便单 Windows {version}", "--notes-file", "-", input_text=notes)
        release_id = run("gh", "release", "view", tag, "--json", "databaseId",
                         "--jq", ".databaseId")

    # Replacing a partial draft upload allows a failed build to be retried.
    run("gh", "release", "upload", tag, str(archive), "--clobber")
    verify_download(tag, package, digest)
    payload = {"body": notes, "draft": False, "make_latest": "legacy"}
    url = run("gh", "api", "--method", "PATCH", f"{endpoint}/{release_id}",
              "--input", "-", "--jq", ".html_url", input_text=json.dumps(payload))
    print(url)


if __name__ == "__main__":
    publish()
