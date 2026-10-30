"""Build/install local Ads Caller UPM releases without running Unity editor code.

Usage: python Tools/adscaller_upm.py build
       python adscaller_upm.py install --project PATH --release PATH --online android --crosspromo
Install requires Unity closed. --migrate-legacy archives overlapping source assets with a rollback receipt.
"""
from pathlib import Path
import argparse, datetime, hashlib, json, os, re, shutil, sys, tarfile, uuid

ROOT=Path(__file__).resolve().parents[1]
BASE=ROOT.parent
PREFIX='com.adscaller.family.'
def release_version(value):
    if re.fullmatch(r'(0|[1-9]\d*)\.(0|[1-9]\d*)', value): value += '.0'
    if not re.fullmatch(r'(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?', value):
        raise ValueError('Invalid package version: '+value)
    return value

def package_name(module):
    return PREFIX + ({'android':'main.android', 'amazon':'main.amazon', 'crosspromo':'cross-promo'}.get(module,module))

def write(p,data):
    p.parent.mkdir(parents=True,exist_ok=True)
    if isinstance(data,str): data=data.encode('utf-8')
    p.write_bytes(data)

def guid(path):
    meta=Path(str(path)+'.meta')
    if not meta.exists(): return None
    m=re.search(r'^guid: (\w+)',meta.read_text(encoding='utf-8-sig'),re.M)
    return m[1] if m else None

def files(project, roots, mapping):
    result=set()
    for rel in roots:
        path=project/mapping.get(rel,rel)
        if not path.exists(): raise RuntimeError('Missing release asset: '+str(path))
        if path.is_dir(): result.update(p for p in path.rglob('*') if p.is_file())
        else: result.add(path)
        if Path(str(path)+'.meta').exists(): result.add(Path(str(path)+'.meta'))
    return result

def roots_from_exporter(name):
    s=(ROOT/'Assets/AdsCaller/Scripts/Editor/AdsCallerPackageExporter.cs').read_text()
    return re.findall(r'"(Assets/[^\"]+)"',s.split('string[] '+name+' =')[1].split('};',1)[0])

def build(output, version=None):
    if version is None:
        prefs = json.loads((ROOT/'UserSettings/AdsCallerPackageExporter.json').read_text(encoding='utf-8-sig'))
        version = prefs['packageVersion']
    CORE = CROSS6 = release_version(version)
    output=output.resolve()
    if output.exists() and any(output.iterdir()): raise RuntimeError('Use a new output folder; previous releases are immutable: '+str(output))
    output.mkdir(parents=True,exist_ok=True)
    catalog={'schema':1,'packages':{},'legacy':{},'created':datetime.datetime.now().isoformat()}
    for variant,project_name in [('android','AdsCaller-Android'),('amazon','TestingAmazonAds'),('crosspromo6','SalonAds-CrossPromo'),('crosspromo2022','SalonAds-CrossPromo-2022')]:
        project=ROOT if variant=='android' else ROOT.parent.parent/'Projects_Amazon/_TestProjects'/({'amazon':'AdsCaller_Amazon','crosspromo6':'AdsCaller-CrossPromo','crosspromo2022':'SalonAds-CrossPromo-2022'}[variant])
        mapping=json.loads((project/'Tools/adscaller-moves.json').read_text())
        selections={}
        if variant=='android':
            selections['core']=(roots_from_exporter('CoreAssets'),CORE,'2022.3',{'com.unity.ugui':'1.0.0'})
            module=json.loads((project/'Assets/AdsCaller/AndroidNetworkModule.json').read_text())
            selections['android']=(module['assets'],CORE,'2022.3',{PREFIX+'core':CORE})
            selections['sdk.max']=(['Assets/MaxSdk'],CORE,'2022.3',{})
            selections['sdk.edm']=(['Assets/ExternalDependencyManager'],CORE,'2022.3',{})
            selections['sdk.admob']=(['Assets/GoogleMobileAds','Assets/Plugins/Android/googlemobileads-unity.aar','Assets/Plugins/Android/GoogleMobileAdsPlugin.androidlib'],CORE,'2022.3',{})
        elif variant=='amazon':
            selections['amazon']=(roots_from_exporter('AmazonNetworkAssets'),CORE,'2022.3',{PREFIX+'core':CORE})
        else:
            old=variant.endswith('2022'); version=CORE if old else CROSS6
            roots=[r for r in roots_from_exporter('CrossPromoAssets') if r!='Assets/AddressableAssetsData']
            selections['crosspromo']=(roots,version,'2022.3' if old else '6000.0',{
                PREFIX+'core':CORE,'com.unity.addressables':'1.29.0' if old else '3.1.0',
                'com.unity.nuget.newtonsoft-json':'3.2.2',
                'com.unity.ugui':'1.0.0' if old else '2.0.0'})
        for module,(roots,version,unity,deps) in selections.items():
            name=package_name(module); key=name+'@'+version
            if module=='crosspromo': key += '-unity'+('2022' if variant.endswith('2022') else '6')
            destination=output/'packages'
            if module=='crosspromo': destination /= 'unity2022' if variant.endswith('2022') else 'unity6'
            destination /= name+'@'+version
            selected=files(project,roots,mapping)
            entries=[]; legacy=[]
            for source in sorted(selected):
                rel=source.relative_to(project).as_posix()
                # Folder GUIDs are not prefab/script identities; leave destination folders independent.
                if source.name.endswith('.meta') and Path(str(source)[:-5]).is_dir(): continue
                # Project-owned SDK credentials/settings are never published in a release.
                if re.search(r'/(AppLovinSettings|GoogleMobileAdsSettings)\.asset(?:\.meta)?$',rel): continue
                if rel.endswith('/AdsCallerAssetIndex.json') or rel.endswith('/AdsCallerAssetIndex.json.meta'): continue
                if '/Tests/' in rel or '/Tests.meta' in rel: continue
                # The Android Resources root also contains Core's shared feedback popup.
                # Publish that subtree only in Core, avoiding duplicate immutable GUIDs.
                if module=='android' and rel.startswith('Assets/AdsCaller/Resources/AdsCallerUI/'): continue
                if module.startswith('sdk.'):
                    target=destination/rel.removeprefix('Assets/')
                else:
                    package_rel=rel.removeprefix('Assets/AdsCaller/').removeprefix('Assets/')
                    package_rel=re.sub(r'^Scripts/Providers/(Android|Amazon)/','Runtime/',package_rel)
                    if module=='crosspromo': package_rel=package_rel.removeprefix('SalonAds/').replace('Scripts/','Runtime/',1)
                    if package_rel.startswith('Editor/OutBuildAutomator.cs'): package_rel=package_rel.replace('Editor/','Editor/Compatibility/',1)
                    target=destination/package_rel
                data=source.read_bytes()
                if rel.endswith('AdsCallerConfig.asset'):
                    # Templates preserve policy but never ship IDs or device identifiers.
                    s=data.decode('utf-8-sig')
                    s=re.sub(r'(?m)^(  (?:Max\w*(?:Id|Key)|AdMob\w*Id(?:_Medium|_Low)?|\w*AdUnitId|\w*[Ss]dk[Kk]ey|testDeviceIds)):.*$',r'\1:',s)
                    s=re.sub(r'(?m)^  useGoogleTestAds: .*$', '  useGoogleTestAds: 1', s)
                    s=re.sub(r'(?m)^  enable(?:Firebase|AppsFlyer): .*$',lambda m:m[0].split(':')[0]+': 0',s)
                    data=s.encode('utf-8')
                write(target,data)
                if source.name.endswith('.meta'): continue
                asset_guid=guid(source)
                if asset_guid:
                    entries.append(dict(path=rel,guid=asset_guid))
                    aliases=[a for a,b in mapping.items() if b==rel]
                    entries.extend(dict(path=a,guid=asset_guid) for a in aliases)
                    legacy.append(dict(path=rel,guid=asset_guid,aliases=aliases,sha256=hashlib.sha256(source.read_bytes()).hexdigest(),configuration=source.suffix=='.asset' and ('/Data/' in rel or '/Database/' in rel or rel.endswith('/AdsConfiguration.asset'))))
            manifest=dict(name=name,version=version,displayName='Ads Caller '+module.replace('sdk.','SDK ').title(),unity=unity,description='Ads Caller '+module+' distribution. Install the matching release set; see AdsCaller-UPM-Guide.md.',author={'name':'Sameer Khan'},dependencies=deps)
            if module in ('android','amazon'):
                sample=ROOT/'Tools/AdsCallerBootstraps'/module.title()
                if not sample.is_dir(): raise RuntimeError('Generate the '+module+' Combined bootstrap sample before building a release')
                shutil.copytree(sample,destination/'Samples~/Combined')
                manifest['samples']=[dict(displayName='Combined bootstrap (requires CrossPromo)',description='Import after installing CrossPromo. Place this single root in the initialization scene and run Ads Caller setup reconciliation after compilation.',path='Samples~/Combined')]
            write(destination/'package.json',json.dumps(manifest,indent=2)+'\n')
            if module=='core':
                write(destination/'Editor/Compatibility/AdsCaller.BuildCompatibility.asmref',json.dumps({'reference':'OutBuildAutomator.Editor'},indent=2)+'\n')
            write(destination/'AdsCallerAssetIndex.json',json.dumps(dict(assets=entries),indent=2)+'\n')
            write(destination/'AdsCallerAssetIndex.json.meta','fileFormatVersion: 2\nguid: '+uuid.uuid5(uuid.NAMESPACE_URL,name+'/index').hex+'\n')
            write(destination/'README.md',(ROOT/'Assets/AdsCaller/AdsCaller-UPM-Guide.md').read_bytes())
            # Registry/tarball packages are immutable to AssetDatabase: every asset needs metadata.
            # New folder/auxiliary GUIDs are package-scoped; original script/prefab GUIDs stay intact.
            for asset in sorted(destination.rglob('*')):
                if asset.name.endswith('.meta'): continue
                metadata=Path(str(asset)+'.meta')
                if metadata.exists(): continue
                asset_guid=uuid.uuid5(uuid.NAMESPACE_URL,name+'/'+asset.relative_to(destination).as_posix()).hex
                write(metadata,'fileFormatVersion: 2\nguid: '+asset_guid+'\n'+('folderAsset: yes\n' if asset.is_dir() else ''))
            # Tarballs and local package folders are both supported; no private registry is assumed.
            archive=output
            if module=='crosspromo': archive /= 'Unity2022' if variant.endswith('2022') else 'Unity6'
            archive.mkdir(parents=True,exist_ok=True)
            archive /= name+'-'+version+'.tgz'
            with tarfile.open(archive,'w:gz') as tar: tar.add(destination,arcname='package')
            catalog['packages'][key]=dict(name=name,version=version,unity=unity,path=destination.relative_to(output).as_posix(),dependencies=deps)
            catalog['legacy'][key]=legacy
            print('BUILT',key,flush=True)
    write(output/'release.json',json.dumps(catalog,indent=2)+'\n')
    shutil.copy2(__file__,output/'adscaller_upm.py')
    shutil.copy2(ROOT/'Assets/AdsCaller/AdsCaller-UPM-Guide.md',output/'README.md')
    print('ADSCALLER_UPM_BUILD_PASS',output)

def install(project,release,online,cross,migrate=False):
    project=project.resolve(); release=release.resolve()
    if not (project/'Assets').is_dir() or not (project/'ProjectSettings/ProjectVersion.txt').exists(): raise RuntimeError('Not a Unity project: '+str(project))
    lock=project/'Temp/UnityLockfile'
    if lock.exists(): raise RuntimeError('Close Unity before external installation: '+str(project))
    editor=re.search(r'm_EditorVersion: (\d+)\.(\d+)',(project/'ProjectSettings/ProjectVersion.txt').read_text())
    if not editor or (int(editor[1]),int(editor[2]))<(2022,3): raise RuntimeError('Unity 2022.3 or newer is required')
    is6=int(editor[1])>=6000
    if not online and not cross: raise RuntimeError('Choose --online android/amazon and/or --crosspromo')
    catalog=json.loads((release/'release.json').read_text()); selected={}
    def add(name,version):
        key=next((k for k,p in catalog['packages'].items() if p['name']==name and p['version']==version and (not name.endswith('cross-promo') or (int(p['unity'].split('.')[0])>=6000)==is6)),name+'@'+version)
        if key not in catalog['packages']: raise RuntimeError('Missing dependency in release: '+key)
        if name in selected: return
        selected[name]=catalog['packages'][key]
        for dep,ver in selected[name]['dependencies'].items():
            if dep.startswith(PREFIX): add(dep,ver)
    def select(name):
        matches=[p for p in catalog['packages'].values() if p['name']==name and
                 (int(p['unity'].split('.')[0]) >= 6000) == is6] if name.endswith('cross-promo') else [p for p in catalog['packages'].values() if p['name']==name]
        if len(matches)!=1: raise RuntimeError('Expected one compatible release for '+name)
        add(name,matches[0]['version'])
    if online: select(package_name(online))
    if cross: select(package_name('crosspromo'))
    manifest_path=project/'Packages/manifest.json'
    manifest=json.loads(manifest_path.read_text()) if manifest_path.exists() else {'dependencies':{}}
    dependencies=manifest.setdefault('dependencies',{})
    other='amazon' if online=='android' else 'android'
    if online and package_name(other) in dependencies: raise RuntimeError('Only one Online provider is supported. Remove '+other+' before switching.')
    if cross:
        pin=dependencies.get('com.unity.addressables','')
        if pin and re.match(r'^\d+\.',pin) and int(pin.split('.')[0])!=(3 if is6 else 1):
            raise RuntimeError('Existing Addressables pin '+pin+' does not match this CrossPromo release. Select the compatible version in Package Manager before installation.')
    # Match GUIDs across all legacy paths, so renamed files cannot leave duplicate scripts/assets.
    source_guids={}
    for m in (project/'Assets').rglob('*.meta'):
        g=guid(Path(str(m)[:-5]))
        if g: source_guids.setdefault(g,[]).append(Path(str(m)[:-5]))
    overlaps=[]
    for item in selected.values():
        key=next(k for k,p in catalog['packages'].items() if p==item)
        for asset in catalog['legacy'][key]:
            for old in source_guids.get(asset['guid'],[]):
                overlaps.append((old,asset))
    if overlaps and not migrate: raise RuntimeError('Legacy Ads Caller assets overlap this release. Back up the project, then use --migrate-legacy to archive matching assets and preserve project configuration.')
    # All checks precede writes. Configuration keeps its original project GUID; package templates get new GUIDs.
    stamp=datetime.datetime.now().strftime('%Y%m%d-%H%M%S-%f')
    backup=project/'AdsCallerMigrationBackups'/stamp
    backup.mkdir(parents=True)
    if manifest_path.exists(): shutil.copy2(manifest_path,backup/'manifest.json')
    receipt={'project':str(project),'release':str(release),'archived':[],'installed':[],'configurationGuidMap':{}}
    embedded=project/'Packages'
    # Local packages live outside Packages and are referenced explicitly, avoiding implicit embedded overrides.
    vendor=project/'AdsCallerPackages'/stamp[-13:].replace('-','')
    try:
        for item in selected.values():
            dest=vendor/item['name'].removeprefix(PREFIX)
            source=(release/item['path']).resolve()
            if not source.is_relative_to(release): raise RuntimeError('Release path escapes release directory')
            shutil.copytree(source,dest)
            receipt['installed'].append(dest.relative_to(project).as_posix())
        for old,asset in overlaps:
            if asset['configuration']:
                original_guid=asset['guid']; new_guid=uuid.uuid5(uuid.NAMESPACE_URL,'gss-template/'+original_guid).hex
                receipt['configurationGuidMap'][original_guid]=new_guid
                continue
            target=backup/'AssetsArchive'/old.relative_to(project)
            target.parent.mkdir(parents=True,exist_ok=True)
            if old.exists(): shutil.move(old,target)
            if Path(str(old)+'.meta').exists(): shutil.move(Path(str(old)+'.meta'),Path(str(target)+'.meta'))
            receipt['archived'].append(old.relative_to(project).as_posix())
        replacements=receipt['configurationGuidMap']
        if replacements:
            for path in vendor.rglob('*'):
                if path.is_file() and path.suffix in ('.meta','.asset','.prefab','.json','.unity'):
                    s=path.read_text(encoding='utf-8-sig')
                    for old,new in replacements.items(): s=s.replace(old,new)
                    write(path,s)
        for item in selected.values():
            dependencies[item['name']]='file:../'+(vendor/item['name'].removeprefix(PREFIX)).relative_to(project).as_posix()
        if is6 and dependencies.get('com.unity.ugui') in (None,'1.0.0'): dependencies['com.unity.ugui']='2.0.0'
        write(manifest_path,json.dumps(manifest,indent=2)+'\n')
        receipt['completed']=True
        write(backup/'receipt.json',json.dumps(receipt,indent=2)+'\n')
    except Exception:
        # Restore original scene/configuration references if any installation step fails.
        for rel in reversed(receipt['archived']):
            src=backup/'AssetsArchive'/rel; dst=project/rel; dst.parent.mkdir(parents=True,exist_ok=True)
            if src.exists(): shutil.move(src,dst)
            if Path(str(src)+'.meta').exists(): shutil.move(Path(str(src)+'.meta'),Path(str(dst)+'.meta'))
        if (backup/'manifest.json').exists(): shutil.copy2(backup/'manifest.json',manifest_path)
        receipt['completed']=False; write(backup/'receipt.json',json.dumps(receipt,indent=2)+'\n')
        raise
    print('ADSCALLER_UPM_INSTALL_PASS',project,'Receipt:',backup/'receipt.json')

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    sub=parser.add_subparsers(dest='command',required=True)
    b=sub.add_parser('build'); b.add_argument('--output',type=Path,default=ROOT/'PackageExports/AdsCaller-UPM'/datetime.datetime.now().strftime('%Y%m%d-%H%M%S'))
    b.add_argument('--version',help='Release version; defaults to Package Exporter preferences')
    i=sub.add_parser('install'); i.add_argument('--project',type=Path,required=True); i.add_argument('--release',type=Path,required=True)
    i.add_argument('--online',choices=['android','amazon']); i.add_argument('--crosspromo',action='store_true'); i.add_argument('--migrate-legacy',action='store_true')
    args=parser.parse_args()
    if args.command=='build': build(args.output,args.version)
    else: install(args.project,args.release,args.online,args.crosspromo,args.migrate_legacy)

if __name__=='__main__': main()
