"""One-shot official pack download, verified extraction and local catalog integration.

This does not open FM or edit its graphics/skin. Progress is durable in assets/download-status.json.
If the server ignores Range, retries verify/reuse the disk prefix but must transfer it again.
"""
from __future__ import annotations
import ctypes
from concurrent.futures import ThreadPoolExecutor
import hashlib
import json
import msvcrt
import os
from pathlib import Path, PurePosixPath
import re
import sqlite3
import struct
import shutil
import subprocess
import sys
import time
import traceback
import zipfile
from PIL import Image

ROOT = Path(__file__).resolve().parent
def option(name):
    try:
        return Path(sys.argv[sys.argv.index(name) + 1])
    except (ValueError, IndexError):
        return None

def resolve_paths():
    configured_state = option("--state-dir")
    location_file = ROOT / "data-location.json"
    if configured_state is not None:
        state = configured_state
    elif location_file.exists():
        state = (ROOT / json.loads(location_file.read_text(encoding="utf-8-sig"))["Root"]).resolve()
    else:
        state = Path(os.environ.get("LOCALAPPDATA", Path.home() / "AppData" / "Local")) / "FM24FaceAssistant"
    assets = option("--assets-dir") or (state / "library-download" if "--state-dir" in sys.argv else ROOT / "assets")
    return state, assets

STATE, ASSETS = resolve_paths()
URL = "https://download.ngregens.org/NG_Regens_MainPack.zip"
EXPECTED = 7313137436
ARCHIVE = ASSETS / "NG_Regens_MainPack.zip"
PART = ASSETS / "NG_Regens_MainPack.zip.part"
STATUS = ASSETS / "download-status.json"
CREATE_NO_WINDOW = 0x08000000

def save(path, obj):
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_name(path.name + ".new")
    with temp.open("w", encoding="utf-8") as f:
        json.dump(obj, f, ensure_ascii=False, indent=2)
    for retry in range(40):
        try:
            os.replace(temp, path)
            break
        except PermissionError:
            if retry==39:raise
            time.sleep(0.05)

def status(stage, **values):
    save(STATUS, dict(stage=stage, pid=os.getpid(), connection_mode="direct", updated=time.strftime("%Y-%m-%d %H:%M:%S"), **values))

def download():
    if ARCHIVE.exists() and ARCHIVE.stat().st_size == EXPECTED:
        return
    # Retain the earlier verified-start probe instead of throwing its data away.
    probe = ASSETS / "range-probe.bin"
    if not PART.exists() and probe.exists():
        os.replace(probe, PART)
    for attempt in range(1, 9):
        existing = PART.stat().st_size if PART.exists() else 0
        if existing > EXPECTED:
            raise ValueError("下载文件大于官方报告大小，已保留文件，请检查下载源版本。")
        if existing == EXPECTED:
            os.replace(PART, ARCHIVE)
            return
        headers = ASSETS / "download.headers"
        status("downloading", downloaded=existing, total=EXPECTED, attempt=attempt,
               note="服务器忽略 Range；重连时核验并保留本地前缀，网络前缀仍需重传。")
        args = ["curl.exe", "--disable", "--noproxy", "*", "--proxy", "", "--fail", "--location", "--silent", "--show-error", "--connect-timeout", "30",
                "--speed-time", "90", "--speed-limit", "1024", "--dump-header", str(headers), URL]
        start = time.monotonic()
        received = 0
        last = 0.0
        with (ASSETS / "curl-errors.log").open("ab") as error:
            direct_env={k:v for k,v in os.environ.items() if k.lower() not in {"http_proxy","https_proxy","all_proxy"}}
            direct_env["NO_PROXY"]="*"
            proc = subprocess.Popen(args, env=direct_env, stdout=subprocess.PIPE, stderr=error, creationflags=CREATE_NO_WINDOW)
            try:
                with PART.open("r+b" if PART.exists() else "w+b") as out:
                    while data := proc.stdout.read(256 * 1024):
                        if received == 0:
                            header = headers.read_text(encoding="utf-8", errors="replace")
                            sizes = re.findall(r"(?im)^Content-Length:\s*(\d+)", header)
                            if sizes and int(sizes[-1]) != EXPECTED:
                                raise ValueError("官方主包大小已改变；未混合不同版本的下载内容。")
                            if not data.startswith(b"PK\x03\x04"):
                                raise ValueError("响应不是 ZIP，停止下载并保留原进度。")
                        prefix = min(len(data), max(0, existing - received))
                        if prefix:
                            out.seek(received)
                            if out.read(prefix) != data[:prefix]:
                                raise ValueError("已下载前缀与服务器不同，可能主包已更新；未覆盖旧进度。")
                        if prefix < len(data):
                            out.seek(received + prefix)
                            out.write(data[prefix:])
                        received += len(data)
                        if received > EXPECTED:
                            raise ValueError("服务器发送的数据超过预计长度。")
                        now = time.monotonic()
                        if now - last >= 3:
                            out.flush()
                            speed = received / max(0.01, now - start)
                            status("downloading", downloaded=max(existing, received), total=EXPECTED,
                                   network_received=received, bytes_per_second=round(speed),
                                   eta_seconds=round((EXPECTED - received) / speed), attempt=attempt,
                                   note="单次持续下载；重试会核验并重传已下载前缀。")
                            last = now
                    out.flush()
                    os.fsync(out.fileno())
                code = proc.wait()
                if code == 0 and PART.stat().st_size == EXPECTED and received == EXPECTED:
                    os.replace(PART, ARCHIVE)
                    return
            finally:
                if proc.poll() is None:
                    proc.terminate()
                    proc.wait()
        status("retrying", downloaded=PART.stat().st_size if PART.exists() else 0,
               total=EXPECTED, attempt=attempt, curl_exit=code)
        time.sleep(min(60, attempt * 5))
    raise IOError("多次连接失败；所有已取得的数据仍保留。再次运行脚本即可重试。")

def classify(relative):
    folders = list(relative.parts[:-1])
    text = "/".join(folders).lower()
    kind = "职员" if "staff" in text else "球员"
    gender = "女" if "female" in text or "women" in text or "wf" in [f.lower() for f in folders] else "男" if "male" in text or "men" in text else "男" if kind == "球员" else "未知"
    generic = {"ng_regens", "ngregens", "players", "player", "staff", "male", "female", "men", "women", "wf"}
    meaningful = [f for f in folders if f.lower() not in generic]
    group = meaningful[-1] if meaningful else "未分类"
    return group, kind, gender

def comparison_path(path):
    text = str(path)
    if text.startswith("\\\\?\\UNC\\"):
        text = "\\\\" + text[8:]
    elif re.match(r"^\\\\\?\\[A-Za-z]:\\", text):
        text = text[4:]
    return Path(text)

def safe_target(destination, relative):
    if relative.is_absolute() or ".." in relative.parts or any(":" in part for part in relative.parts):
        raise ValueError(f"压缩包含有越界路径，已停止解压：{relative}")
    # Windows can return the same path with or without its extended-length prefix.
    # Compare identical namespaces, retaining the directory-boundary check.
    destination = comparison_path(destination)
    for attempt in range(3):
        target = comparison_path((destination / str(relative)).resolve())
        if target.is_relative_to(destination):
            return target
        if attempt < 2:
            time.sleep(0.05)
    raise ValueError(f"压缩包含有越界路径，已停止解压：{relative} -> {target}; root={destination}")

def extract_and_index():
    status("preparing", note="正在检查本地压缩包和可用空间；无需重新下载。")
    destination = ASSETS / "NG_Regens_Library"
    destination.mkdir(exist_ok=True)
    destination = comparison_path(destination.resolve())
    with zipfile.ZipFile(ARCHIVE) as archive:
        entries = archive.infolist()
        images = [e for e in entries if not e.is_dir() and PurePosixPath(e.filename).suffix.lower() in {".png", ".jpg", ".jpeg"} and classify(PurePosixPath(e.filename.replace("\\", "/")))[1:] != ("球员", "女")]
        if not images:
            raise ValueError("压缩包内没有头像图片。")
        remaining = sum(e.file_size for e in images if not (destination / e.filename).exists())
        import shutil
        if shutil.disk_usage(destination).free < remaining + 1024**3:
            raise IOError("解压空间不足，至少需要剩余解压大小另加 1 GB。")
        rows = []
        seen = set()
        raw_seen = set()
        duplicate = 0
        counts = {}
        # Durable per-image checkpoints survive abrupt process termination. They are
        # valid only for this archive version and unchanged extracted files.
        fingerprint = f"v1:{ARCHIVE.stat().st_size}:{ARCHIVE.stat().st_mtime_ns}"
        checkpoint = sqlite3.connect(ASSETS / "index-checkpoint.sqlite")
        checkpoint.execute("CREATE TABLE IF NOT EXISTS metadata (fingerprint TEXT)")
        checkpoint.execute("CREATE TABLE IF NOT EXISTS images (name TEXT PRIMARY KEY, crc INTEGER, size INTEGER, mtime TEXT, row_json TEXT)")
        previous = checkpoint.execute("SELECT fingerprint FROM metadata").fetchone()
        if previous != (fingerprint,):
            checkpoint.execute("DELETE FROM images")
            checkpoint.execute("DELETE FROM metadata")
            checkpoint.execute("INSERT INTO metadata VALUES (?)", (fingerprint,))
            checkpoint.commit()
        def process(entry):
            relative = PurePosixPath(entry.filename.replace("\\", "/"))
            target = safe_target(destination, relative)
            # Reading to EOF checks ZIP's CRC32 even for files already extracted by a previous run.
            data = archive.read(entry)
            if len(data) != entry.file_size:
                raise ValueError(f"解压长度不符：{entry.filename}")
            raw_hash = hashlib.sha256(data).hexdigest().upper()
            if not target.exists() or hashlib.sha256(target.read_bytes()).hexdigest().upper() != raw_hash:
                target.parent.mkdir(parents=True, exist_ok=True)
                temp = target.with_name(target.name + ".new")
                temp.write_bytes(data)
                os.replace(temp, target)
            with Image.open(target) as image:
                image.load()
                with image.convert("RGBA") as rgba:
                    pixels = rgba.tobytes("raw", "BGRA")
                    visual_hash = hashlib.sha256(struct.pack("<II", *rgba.size) + pixels).hexdigest().upper()
            group, kind, gender = classify(relative)
            return dict(Path=str(target), Group=group, Kind=kind, Gender=gender, Hash=raw_hash, VisualHash=visual_hash)
        restored = 0
        try:
            status("indexing", processed=0, total_images=len(images), unique_images=0,
                   duplicate_images=0, restored_images=0, note="正在校验图片；已校验且未变化的断点将直接复用。")
            # Bound outstanding work to 128 images; do not allocate hundreds of
            # thousands of futures or decoded images at once.
            with ThreadPoolExecutor(max_workers=4) as pool:
                for start in range(0, len(images), 128):
                    batch = images[start:start + 128]
                    pending = {}
                    for entry in batch:
                        cached = checkpoint.execute("SELECT crc,size,mtime,row_json FROM images WHERE name=?", (entry.filename,)).fetchone()
                        row = json.loads(cached[3]) if cached else None
                        target = Path(row["Path"]) if row else None
                        stat = target.stat() if target and target.is_file() else None
                        if cached and cached[:2] == (entry.CRC, entry.file_size) and stat and stat.st_size == entry.file_size and str(stat.st_mtime_ns) == cached[2]:
                            pending[entry.filename] = row
                            restored += 1
                        else:
                            pending[entry.filename] = pool.submit(process, entry)
                    for entry in batch:
                        value = pending[entry.filename]
                        row = value if isinstance(value, dict) else value.result()
                        stat = Path(row["Path"]).stat()
                        checkpoint.execute("INSERT OR REPLACE INTO images VALUES (?,?,?,?,?)", (entry.filename, entry.CRC, entry.file_size, str(stat.st_mtime_ns), json.dumps(row, ensure_ascii=False)))
                        raw_seen.add(row["Hash"])
                        if row["VisualHash"] in seen:
                            duplicate += 1
                        seen.add(row["VisualHash"])
                        key = f"{row['Kind']}/{row['Gender']}/{row['Group']}"
                        counts[key] = counts.get(key, 0) + 1
                        rows.append(row)
                    checkpoint.commit()
                    status("indexing", processed=len(rows), total_images=len(images), unique_images=len(seen), duplicate_images=duplicate, restored_images=restored)
        finally:
            checkpoint.close()
        if not rows:
            raise ValueError("未得到可用头像。")
    save(destination / "faces.json", rows)
    # The catalog is cached independently; no player assignment or graphics mappings are changed.
    STATE.mkdir(parents=True, exist_ok=True)
    save(STATE / "catalog.json", rows)
    if "--managed" not in sys.argv:
        settings_path = STATE / "settings.json"
        settings = json.loads(settings_path.read_text(encoding="utf-8-sig")) if settings_path.exists() else {}
        settings["Pack"] = str(destination)
        save(settings_path, settings)
    report = dict(source=URL, archive=str(ARCHIVE), archive_bytes=ARCHIVE.stat().st_size,
                  crc="every FM24-compatible image entry verified", total_images=len(rows), unique_files=len(raw_seen),
                  unique_pixels=len(seen), duplicate_pixels=duplicate, groups=counts, library=str(destination), state_directory=str(STATE))
    save(ASSETS / "library-report.json", report)
    status("complete", **report, note="重启头像助手即可加载图库；未修改任何人物映射、原皮肤或存档。")

def main():
    ASSETS.mkdir(parents=True, exist_ok=True)
    with (ASSETS / "download.lock").open("a+b") as lock:
        lock.seek(0)
        if lock.read(1) == b"":
            lock.write(b"0")
            lock.flush()
        lock.seek(0)
        try:
            msvcrt.locking(lock.fileno(), msvcrt.LK_NBLCK, 1)
        except OSError:
            print("已有图库下载进程，未重复启动。", flush=True)
            return
        log = (ASSETS / "worker.log").open("a", encoding="utf-8", buffering=1)
        sys.stdout = sys.stderr = log
        print(f"[{time.strftime('%Y-%m-%d %H:%M:%S')}] worker started pid={os.getpid()}")
        try:
            # Keep a long one-shot download alive while the screen is idle; restore on exit.
            ctypes.windll.kernel32.SetThreadExecutionState(0x80000001)
            report_path = ASSETS / "library-report.json"
            if not ARCHIVE.exists() and not PART.exists() and report_path.exists() and (ASSETS / "NG_Regens_Library" / "faces.json").exists():
                report = json.loads(report_path.read_text(encoding="utf-8-sig"))
                STATE.mkdir(parents=True, exist_ok=True)
                if not (STATE / "catalog.json").exists():
                    shutil.copyfile(ASSETS / "NG_Regens_Library" / "faces.json", STATE / "catalog.json")
                status("complete", **report, note="完整图库已经就绪，无需重新下载。")
                return
            download()
            extract_and_index()
        except Exception as error:
            traceback.print_exc()
            previous = json.loads(STATUS.read_text(encoding="utf-8")) if STATUS.exists() else {}
            previous.pop("stage", None)
            previous.pop("pid", None)
            previous.pop("updated", None)
            previous.pop("connection_mode", None)
            previous.update(error=str(error), downloaded=ARCHIVE.stat().st_size if ARCHIVE.exists() else PART.stat().st_size if PART.exists() else 0, total=EXPECTED, detail=traceback.format_exc())
            status("failed", **previous)
            raise
        finally:
            ctypes.windll.kernel32.SetThreadExecutionState(0x80000000)
            print(f"[{time.strftime('%Y-%m-%d %H:%M:%S')}] worker exited")

if __name__ == "__main__":
    main()
