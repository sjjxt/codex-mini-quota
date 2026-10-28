"""Edit only the startup flag, saving its prior value for explicit restoration."""
from pathlib import Path
import argparse
import hashlib
import json
import os
import re
import tempfile
import tomllib


def config_path():
    return Path(os.environ.get('CODEX_HOME') or Path.home() / '.codex').expanduser().resolve() / 'config.toml'


def read_config(path):
    raw = path.read_bytes()
    text = raw.decode('utf-8-sig')
    desktop = tomllib.loads(text).get('desktop', {})
    if not isinstance(desktop, dict):
        raise ValueError('desktop must be a TOML table')
    value = desktop.get('mcp_startup_authentication')
    if value is not None and not isinstance(value, bool):
        raise ValueError('startup setting must be boolean')
    return raw, text, value


def change_flag(text, value):
    if tomllib.loads(text).get('desktop', {}).get('mcp_startup_authentication') is value:
        return text
    nl = '\r\n' if '\r\n' in text else '\n'
    header = re.search(r'(?m)^[ \t]*\[[ \t]*(?:desktop|"desktop"|\'desktop\')[ \t]*\][ \t]*(?:#.*)?\r?$', text)
    if header:
        start = header.end()
        following = re.search(r'(?m)^[ \t]*\[', text[start:])
        end = start + following.start() if following else len(text)
        prefix, body, suffix = text[:start], text[start:end], text[end:]
        name = r'(?:mcp_startup_authentication|"mcp_startup_authentication"|\'mcp_startup_authentication\')'
    else:
        first = re.search(r'(?m)^[ \t]*\[', text)
        end = first.start() if first else len(text)
        prefix, body, suffix = '', text[:end], text[end:]
        name = r'desktop[ \t]*\.[ \t]*mcp_startup_authentication'
    pattern = re.compile(r'(?m)^([ \t]*' + name + r'[ \t]*=[ \t]*)(true|false)([ \t]*(?:#.*)?\r?$)')
    if pattern.search(body):
        body = pattern.sub('' if value is None else lambda m: m[1] + str(value).lower() + m[3], body, count=1)
        changed = prefix + body + suffix
    elif value is None:
        changed = text
    elif header:
        changed = prefix + nl + 'mcp_startup_authentication = ' + str(value).lower() + body + suffix
    else:
        changed = text.rstrip('\r\n') + nl + nl + '[desktop]' + nl + 'mcp_startup_authentication = ' + str(value).lower() + nl
    if tomllib.loads(changed).get('desktop', {}).get('mcp_startup_authentication') is not value:
        raise ValueError('Unsupported TOML spelling; set the startup flag manually. No changes written.')
    return changed


def atomic_write(path, data, expected=None):
    path.parent.mkdir(parents=True, exist_ok=True)
    descriptor, temporary = tempfile.mkstemp(prefix='.' + path.name + '.', dir=path.parent)
    temporary = Path(temporary)
    try:
        with os.fdopen(descriptor, 'wb') as stream:
            stream.write(data)
        if expected is not None and path.read_bytes() != expected:
            raise RuntimeError('Configuration changed while preparing update; no changes written.')
        os.replace(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)


def state_path(config):
    local = Path(os.environ.get('LOCALAPPDATA') or Path.home() / 'AppData/Local') / 'CodexMiniQuota'
    digest = hashlib.sha256(str(config).casefold().encode()).hexdigest()[:16]
    return local / ('startup-setting-' + digest + '.json')


def manage(action):
    config = config_path()
    raw, text, previous = read_config(config)
    if action == 'marketplace':
        record = tomllib.loads(text).get('marketplaces', {}).get('mini-quota-local')
        if record is None:
            print('null')
            return
        if not isinstance(record, dict) or record.get('source_type') != 'local' or not isinstance(record.get('source'), str):
            raise ValueError('Cannot migrate a non-local Mini Quota source automatically; no changes written.')
        print(json.dumps({'name': 'mini-quota-local', 'root': record['source']}, ensure_ascii=True))
        return
    state_file = state_path(config)
    if action == 'check':
        change_flag(text, True)
        if not os.access(config, os.W_OK) or not os.access(config.parent, os.W_OK):
            raise PermissionError('Codex config is not writable')
        print(json.dumps({'check': 'ok', 'startupCheck': previous}))
        return
    if action == 'restore':
        if not state_file.exists():
            print('No saved startup setting; configuration left unchanged.')
            return
        state = json.loads(state_file.read_text(encoding='utf-8'))
        if state.get('config') != str(config):
            raise ValueError('Saved setting belongs to a different config')
        if previous is not True:
            print('Startup setting changed after install; left unchanged.')
            state_file.unlink()
            return
        changed = change_flag(text, state['previous'])
    else:
        changed = change_flag(text, True)
        if not state_file.exists():
            atomic_write(state_file, json.dumps({'config': str(config), 'previous': previous}, indent=2).encode())
    if changed != text:
        encoded = (b'\xef\xbb\xbf' if raw.startswith(b'\xef\xbb\xbf') else b'') + changed.encode('utf-8')
        atomic_write(config, encoded, expected=raw)
    if action == 'restore':
        state_file.unlink()
        print('Restored the previous MCP startup setting.')
    else:
        print('Enabled global MCP startup checks. Previous setting saved.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--action', choices=('check', 'enable', 'restore', 'marketplace'), default='enable')
    manage(parser.parse_args().action)
