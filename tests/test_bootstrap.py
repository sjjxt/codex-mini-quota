"""MCP protocol smoke test with no quota executable present; no model/network call."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

root = Path(__file__).resolve().parents[1]
with tempfile.TemporaryDirectory(dir=sys.argv[1]) as folder:
    executable = Path(folder) / 'QuotaBootstrap.exe'
    shutil.copy2(root / 'plugins/mini-quota/bin/QuotaBootstrap.exe', executable)
    requests = [
        {'jsonrpc': '2.0', 'id': 1, 'method': 'initialize', 'params': {'protocolVersion': '2024-11-05', 'clientInfo': {'name': 'quota-regression'}}},
        {'jsonrpc': '2.0', 'id': 2, 'method': 'tools/list'},
        {'jsonrpc': '2.0', 'id': 3, 'method': 'resources/templates/list'},
        {'jsonrpc': '2.0', 'id': 4, 'method': 'tools/call', 'params': {'name': 'does-not-exist'}},
    ]
    result = subprocess.run([executable], input='\n'.join(json.dumps(r) for r in requests) + '\n', text=True, capture_output=True, timeout=10)
    assert result.returncode == 0, result.stderr
    replies = [json.loads(line) for line in result.stdout.splitlines()]
    expected = json.loads((root / 'plugins/mini-quota/plugin.json').read_text(encoding='utf-8-sig'))['version']
    assert replies[0]['result']['serverInfo']['version'] == expected
    assert replies[1]['result']['tools'][0]['name'] == 'quota_component_status'
    assert replies[2]['result']['resourceTemplates'] == []
    assert replies[3]['result']['isError'] is True
    print('MCP smoke checks: 4 passed')
