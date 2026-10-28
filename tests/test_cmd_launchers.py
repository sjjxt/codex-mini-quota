"""Integration check for double-click launchers. Uses an isolated CODEX_HOME.

No account credentials are copied. Does not start or uninstall the real helper.
"""
import json,os,shutil,subprocess,sys,tomllib,time
from pathlib import Path

root=Path(__file__).resolve().parents[1]
work=Path(sys.argv[1]).resolve()
stage=work/'CMD check & 中文目录'
if stage.exists():raise RuntimeError('Use a new verification directory')
stage.parent.mkdir(parents=True,exist_ok=True)
shutil.copytree(root,stage,ignore=shutil.ignore_patterns('.git','__pycache__','.test-build','test-results'))
home=work/'isolated-home';home.mkdir()
config=home/'config.toml';config.write_text('[desktop]\nmcp_startup_authentication = false\n',encoding='utf-8')
env=os.environ.copy();env['CODEX_HOME']=str(home)
# Model Explorer double-clicks rather than inheriting Codex's injected PATH.
windows=Path(os.environ['SystemRoot'])
env['PATH']=os.pathsep.join(str(p) for p in [windows/'System32',windows,windows/'System32/WindowsPowerShell/v1.0'])
probe=subprocess.run([str(windows/'System32/where.exe'),'codex'],env=env,cwd=stage,capture_output=True)
assert probe.returncode!=0, 'Test environment unexpectedly resolves codex from PATH'
print('PASS desktop-like PATH has no codex command',flush=True)
passed=0
def run(file,keys,expect=0):
    global passed
    result=subprocess.run([os.environ.get('COMSPEC','cmd.exe'),'/d','/c',file],cwd=stage,env=env,input=keys.encode('ascii'),capture_output=True,timeout=60)
    if (result.returncode==0 if expect=='nonzero' else result.returncode!=expect):
        raise RuntimeError(f'{file} exit={result.returncode}; '+result.stdout[-1800:].decode(errors='replace')+result.stderr[-1800:].decode(errors='replace'))
    passed+=1
def flag():return tomllib.loads(config.read_text(encoding='utf-8-sig'))['desktop']['mcp_startup_authentication']
run('uninstall.cmd','0\r\n');assert flag() is False
print('PASS cancellation keeps original setting',flush=True)
run('install.cmd','\r\n');assert flag() is True
version=json.loads((stage/'plugins/mini-quota/plugin.json').read_text(encoding='utf-8-sig'))['version']
assert (home/'plugins/cache/mini-quota-local/mini-quota'/version/'bin/MiniQuota.exe').is_file()
print('PASS double-click installer: Chinese, spaces and ampersand path',flush=True)
run('uninstall.cmd','1\r\n\r\n');assert flag() is True
print('PASS uninstall option 1 preserves global setting',flush=True)
run('install.cmd','\r\n');assert flag() is True
run('uninstall.cmd','2\r\n\r\n');assert flag() is False
print('PASS uninstall option 2 restores original false',flush=True)
(stage/'install.ps1').rename(stage/'install.ps1.disabled')
run('install.cmd','\r\n',expect='nonzero')
assert flag() is False
print('PASS installer failure preserves nonzero exit code',flush=True)
(stage/'install.ps1.disabled').rename(stage/'install.ps1')
run('install.cmd','\r\n');assert flag() is True
# Reproduce removal after old versions remain loaded. Also prove that a process
# with the same filename outside this plugin's cache is not stopped.
holder=work/'CacheHolder.exe'
compiler=windows/'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
subprocess.run([compiler,'/nologo','/target:winexe','/out:'+str(holder),stage/'tests/CacheHolder.cs'],check=True,capture_output=True)
legacy=home/'plugins/cache/mini-quota-local/mini-quota/0.0-cache-test/bin'
legacy.mkdir(parents=True)
foreign=work/'foreign-plugin';foreign.mkdir()
owned=[];other=None
try:
    for name in ('MiniQuota.exe','QuotaBootstrap.exe'):
        target=legacy/name;shutil.copy2(holder,target)
        owned.append(subprocess.Popen([target],creationflags=subprocess.CREATE_NO_WINDOW))
    target=foreign/'QuotaBootstrap.exe';shutil.copy2(holder,target)
    other=subprocess.Popen([target],creationflags=subprocess.CREATE_NO_WINDOW)
    time.sleep(.5)
    # Make the same first failed removal as the old script: it may remove the
    # config entry before failing to delete locked files. New uninstall retries
    # must work even after that partial operation.
    command='. .\\resolve-codex.ps1; $c=Resolve-MiniQuotaCodex; & $c plugin remove mini-quota@mini-quota-local --json; exit $LASTEXITCODE'
    first=subprocess.run([windows/'System32/WindowsPowerShell/v1.0/powershell.exe','-NoProfile','-ExecutionPolicy','Bypass','-Command',command],cwd=stage,env=env,capture_output=True,timeout=30)
    assert first.returncode!=0 and legacy.exists(), 'Expected locked-cache removal failure'
    run('uninstall.cmd','2\r\n\r\n')
    assert flag() is False
    assert all(p.wait(timeout=4) is not None for p in owned)
    assert other.poll() is None, 'Unrelated process with same filename was stopped'
    assert not (home/'plugins/cache/mini-quota-local/mini-quota').exists(), 'Plugin cache left behind'
    print('PASS locked legacy processes stopped; partial uninstall recovered; original setting restored',flush=True)
    print('PASS same-name process outside target cache preserved',flush=True)
finally:
    for process in owned+([other] if other else []):
        if process.poll() is None:process.terminate();process.wait(timeout=4)

def cli(arguments):
    resolver='. .\\resolve-codex.ps1; $c=Resolve-MiniQuotaCodex; '
    result=subprocess.run([windows/'System32/WindowsPowerShell/v1.0/powershell.exe','-NoProfile','-ExecutionPolicy','Bypass','-Command',resolver+arguments+'; exit $LASTEXITCODE'],cwd=stage,env=env,capture_output=True,timeout=45)
    if result.returncode:raise RuntimeError('CLI fixture failed: '+result.stderr[-1500:].decode(errors='replace'))
    return json.loads(result.stdout)
def marketplaces():return {m['name']:m['root'] for m in cli('& $c plugin marketplace list --json')['marketplaces']}
assert 'mini-quota-local' not in marketplaces(), 'Uninstall left a source registration behind'
# Model a legacy uninstall which left its old source registered.
unrelated=work/'unrelated-marketplace'
manifest=unrelated/'.agents/plugins/marketplace.json';manifest.parent.mkdir(parents=True)
manifest.write_text(json.dumps({'name':'unrelated-test','plugins':[]}),encoding='utf-8')
cli("& $c plugin marketplace add '"+str(unrelated).replace("'","''")+"' --json")
cli('& $c plugin marketplace add . --json')
cli('& $c plugin add mini-quota@mini-quota-local --json')
old_stage=stage
stage=work/'new version & 新目录'
shutil.copytree(old_stage,stage)
legacy.mkdir(parents=True)
target=legacy/'QuotaBootstrap.exe';shutil.copy2(holder,target)
locked=subprocess.Popen([target],creationflags=subprocess.CREATE_NO_WINDOW)
try:
    time.sleep(.3)
    run('install.cmd','\r\n')
    assert locked.wait(timeout=4) is not None
    listing=marketplaces()
    assert Path(listing['mini-quota-local']).resolve()==stage.resolve()
    assert Path(listing['unrelated-test']).resolve()==unrelated.resolve()
    run('install.cmd','\r\n')
    print('PASS changing folders migrates legacy source and releases loaded cache; same-folder reinstall succeeds',flush=True)
finally:
    if locked.poll() is None:locked.terminate();locked.wait(timeout=4)
run('uninstall.cmd','2\r\n\r\n');assert flag() is False
assert 'mini-quota-local' not in marketplaces() and 'unrelated-test' in marketplaces()
# Now test a registered source folder that no longer exists.
cli('& $c plugin marketplace add . --json')
relocated=work/'moved directory & 再移动'
assert stage.resolve().is_relative_to(work) and relocated.resolve().is_relative_to(work)
stage.rename(relocated);stage=relocated
run('install.cmd','\r\n');assert flag() is True
assert Path(marketplaces()['mini-quota-local']).resolve()==stage.resolve()
run('uninstall.cmd','2\r\n\r\n');assert flag() is False
assert 'mini-quota-local' not in marketplaces() and 'unrelated-test' in marketplaces()
print('PASS missing old folder recovered; uninstall cleans own source; unrelated marketplace preserved',flush=True)
print(json.dumps({'cmdLauncherRuns':passed,'result':'pass'}),flush=True)
