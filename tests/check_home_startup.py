"""Opt-in integration check: real MCP inventory, no chat or model request.

Requires the plugin to be installed and Codex Desktop to be running.
Restarts only the quota companion. Keep Codex open throughout this check.
"""
import argparse
import csv
import json
import os
from pathlib import Path
import queue
import shutil
import subprocess
import tempfile
import threading
import time

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--allow-component-restart', action='store_true')
parser.add_argument('--work-directory', type=Path)
options = parser.parse_args()
if not options.allow_component_restart:
    parser.error('This check restarts the quota companion. Pass --allow-component-restart explicitly.')
root = Path(__file__).resolve().parents[1]
ui = root / 'plugins/mini-quota/bin/MiniQuota.exe'
local = Path(os.environ['LOCALAPPDATA'])
candidates = list((local / 'OpenAI/Codex/bin').glob('*/codex.exe'))
exe = max(candidates, key=lambda p: p.stat().st_mtime) if candidates else shutil.which('codex')
if not exe:
    raise RuntimeError('Codex app-server was not found')
work = options.work_directory or Path(tempfile.gettempdir()) / 'mini-quota-home-check'
work.mkdir(parents=True, exist_ok=True)
subprocess.run([ui, '--quit'], check=True, creationflags=subprocess.CREATE_NO_WINDOW)
time.sleep(2)


def helpers():
    result = subprocess.run(['tasklist', '/FI', 'IMAGENAME eq MiniQuota.exe', '/FO', 'CSV', '/NH'], capture_output=True, text=True, creationflags=subprocess.CREATE_NO_WINDOW)
    return sorted(int(row[1]) for row in csv.reader(result.stdout.splitlines()) if len(row) > 1 and row[0].lower() == 'miniquota.exe')


assert not helpers(), 'Companion did not exit; no inventory request sent'
environment = {k: v for k, v in os.environ.items() if not k.startswith('CODEX_') or k == 'CODEX_HOME'}
with (work / 'app-server-stderr.log').open('w', encoding='utf-8') as stderr:
    process = subprocess.Popen([exe, 'app-server', '--listen', 'stdio://'], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=stderr, text=True, encoding='utf-8', env=environment, creationflags=subprocess.CREATE_NO_WINDOW)
    messages = queue.Queue()

    def reader():
        for line in process.stdout:
            try:
                messages.put(json.loads(line))
            except ValueError:
                pass

    threading.Thread(target=reader, daemon=True).start()

    def request(request_id, method, params):
        process.stdin.write(json.dumps({'id': request_id, 'method': method, 'params': params}) + '\n')
        process.stdin.flush()
        end = time.monotonic() + 45
        while time.monotonic() < end:
            response = messages.get(timeout=max(.1, end - time.monotonic()))
            if response.get('id') == request_id:
                assert 'result' in response, 'RPC request failed'
                return response['result']
        raise TimeoutError(method)

    try:
        request(1, 'initialize', {'clientInfo': {'name': 'mini_quota_home_check', 'version': '1'}})
        process.stdin.write('{"method":"initialized"}\n')
        process.stdin.flush()
        request(2, 'mcpServerStatus/list', {'cursor': None, 'limit': 100, 'detail': 'toolsAndAuthOnly'})
        started = helpers()
        assert len(started) == 1, 'Expected one companion after inventory returned'
        print('PASS started from zero without a chat')
        time.sleep(3)
        assert process.poll() is None and helpers() == started, 'Companion did not survive inventory cleanup'
        print('PASS same companion survives inventory cleanup')
    finally:
        process.kill()
        process.wait()
    time.sleep(2)
    assert helpers() == started, 'Companion did not survive the probe connection closing'
    print('PASS same companion survives probe exit')
