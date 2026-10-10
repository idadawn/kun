#!/usr/bin/env python3
"""拒绝明文会话日志和产品专属默认值；兼容 API/显式旧 wire 名称不视为默认值。"""
from pathlib import Path
import re
import sys

root = Path(__file__).resolve().parents[1]
failures = []
for path in (root / "src").rglob("*.cs"):
    if "obj" in path.parts or "bin" in path.parts:
        continue
    source = path.read_text(encoding="utf-8-sig")
    code = "\n".join(line for line in source.splitlines() if not line.lstrip().startswith("//"))
    if re.search(r'=\s*"[^"\n]*(?:neuroagent|iclaw)', code, re.I):
        failures.append(f"{path.relative_to(root)}: 产品名称不能作为框架默认值")
    if "Kun.Identity" not in str(path):
        continue
    if re.search(r'\{(?:SessionId|Sid|Cookie)\}', source, re.I):
        failures.append(f"{path.relative_to(root)}: 会话日志模板必须使用哈希标识")
    if re.search(r'(?:Log\.[A-Za-z]+|_logger\.Log[A-Za-z]+)\([^;]*,\s*(?:cookie|sid|sessionId)\s*[,)]', source, re.I):
        failures.append(f"{path.relative_to(root)}: 日志调用不能传入明文会话标识")
for path in (root / "src").rglob("*.csproj"):
    if re.search(r'<Description>[^<]*(?:neuroagent|iclaw)', path.read_text(), re.I):
        failures.append(f"{path.relative_to(root)}: 发布包说明必须与下游产品解耦")
if failures:
    print("\n".join(failures))
    sys.exit(1)
print("OK: identity log privacy and product-neutral defaults")
