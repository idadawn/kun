#!/usr/bin/env python3
"""发布版本与包清单预检；只读取版本和 NuGet 元数据，不发布或读取凭据。"""

from __future__ import annotations

import argparse
import datetime
import json
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET
import zipfile


VERSION = re.compile(r"(?P<year>[1-9][0-9]{3})\.(?P<month>[1-9][0-9]?)\.(?P<day>[1-9][0-9]?)(?:-(?P<kind>hotfix|beta)\.(?P<number>[1-9][0-9]*))?")
PACKAGE_ID = re.compile(r"Kun\.[A-Za-z0-9_.-]{1,96}", re.IGNORECASE | re.ASCII)


def release_info(value: str, *, tag: bool = False) -> dict:
    if tag:
        if not value.startswith("v"):
            raise ValueError("发布标签必须以 v 开头。")
        value = value[1:]
    match = VERSION.fullmatch(value)
    if match is None:
        raise ValueError("版本必须为 YYYY.M.D、YYYY.M.D-hotfix.N 或 YYYY.M.D-beta.N；不允许纯数字后缀、前导零或空白。")
    try:
        datetime.date(int(match["year"]), int(match["month"]), int(match["day"]))
    except ValueError as error:
        raise ValueError("版本中的日期无效。") from error
    return {"version": value, "kind": match["kind"] or "stable", "prerelease": match["kind"] == "beta"}


def validate_artifacts(directory: Path, version: str) -> int:
    release_info(version)
    if not directory.is_dir() or directory.is_symlink():
        raise ValueError("包目录不存在或不是普通目录。")
    files = sorted([*directory.glob("*.nupkg"), *directory.glob("*.snupkg")])
    packages: dict[str, str] = {}
    manifests = []
    for file in files:
        if not file.is_file() or file.is_symlink():
            raise ValueError("包文件必须为普通文件。")
        try:
            with zipfile.ZipFile(file) as archive:
                entries = [entry for entry in archive.infolist() if "/" not in entry.filename and entry.filename.endswith(".nuspec")]
                if len(entries) != 1 or entries[0].file_size > 1_048_576:
                    raise ValueError("包必须包含唯一、大小受限的根 nuspec。")
                raw = archive.read(entries[0])
            if b"<!DOCTYPE" in raw or b"<!ENTITY" in raw:
                raise ValueError("包清单不允许 DTD 或实体声明。")
            root = ET.fromstring(raw)
        except (zipfile.BadZipFile, ET.ParseError, OSError) as error:
            raise ValueError("无法读取合法包清单。") from error
        metadata = root.find("{*}metadata")
        if root.tag.rsplit("}", 1)[-1] != "package" or metadata is None:
            raise ValueError("包清单缺少 package/metadata。")
        identifier = metadata.findtext("{*}id", "")
        actual = metadata.findtext("{*}version", "")
        if PACKAGE_ID.fullmatch(identifier) is None:
            raise ValueError("发行物中出现无效的 Kun 包标识。")
        if actual != version:
            raise ValueError(f"{identifier} 的包版本与标签不一致。")
        if file.suffix == ".nupkg":
            key = identifier.casefold()
            if key in packages:
                raise ValueError("同一包出现重复的发行物。")
            packages[key] = identifier
        manifests.append((identifier, metadata, file.suffix))
    if not packages:
        raise ValueError("没有可发布的 nupkg。")
    for identifier, metadata, suffix in manifests:
        if suffix == ".snupkg" and identifier.casefold() not in packages:
            raise ValueError("符号包没有对应的发行包。")
        for dependency in metadata.findall(".//{*}dependency"):
            name = dependency.attrib.get("id", "")
            if not name.casefold().startswith("kun."):
                continue
            value = re.sub(r"\s+", "", dependency.attrib.get("version", ""))
            if value not in {version, f"[{version}]", f"[{version},)"}:
                raise ValueError(f"{identifier} 的 Kun 依赖未锁步到本次发行版本。")
            if name.casefold() not in packages:
                raise ValueError(f"{identifier} 的 Kun 依赖缺少本次发行物。")
    return len(packages)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    source = parser.add_mutually_exclusive_group(required=True)
    source.add_argument("--tag")
    source.add_argument("--version")
    parser.add_argument("--artifacts", type=Path)
    parser.add_argument("--github-output", type=Path)
    args = parser.parse_args()
    try:
        info = release_info(args.tag if args.tag is not None else args.version, tag=args.tag is not None)
        if args.artifacts is not None:
            info["packages"] = validate_artifacts(args.artifacts, info["version"])
        if args.github_output is not None:
            with args.github_output.open("a", encoding="utf-8") as output:
                output.write(f"version={info['version']}\nprerelease={str(info['prerelease']).lower()}\n")
        print(json.dumps(info, ensure_ascii=False))
        return 0
    except (ValueError, OSError) as error:
        print(f"发布预检失败：{error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
