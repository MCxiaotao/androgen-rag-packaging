from __future__ import annotations

import hashlib
import json
import logging
import os
import shutil
import socket
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request
import webbrowser
import zipfile
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path


APP_STATE_SUBDIRS = [
    "cache",
    "cache/downloads",
    "cache/manifests",
    "cache/temp",
    "config",
    "archive",
    "input",
    "kb",
    "logs",
    "logs/app",
    "logs/launcher",
    "output",
    "packages",
    "packages/versions",
    "runs",
]


@dataclass
class LauncherSettings:
    app_id: str
    display_name: str
    channel: str
    bootstrap_version: str
    manifest_url: str
    update_enabled: bool
    open_browser: bool
    default_port: int
    request_timeout_seconds: int


def is_frozen() -> bool:
    return bool(getattr(sys, "frozen", False))


def install_root() -> Path:
    if is_frozen():
        return Path(sys.executable).resolve().parent
    return Path(__file__).resolve().parent.parent


def utc_now_iso() -> str:
    return datetime.now(timezone.utc).replace(microsecond=0).isoformat()


def read_json(path: Path, default: dict | None = None) -> dict:
    if not path.exists():
        return {} if default is None else dict(default)
    with path.open("r", encoding="utf-8") as handle:
        return json.load(handle)


def write_json_atomic(path: Path, payload: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile("w", encoding="utf-8", delete=False, dir=str(path.parent)) as handle:
        json.dump(payload, handle, ensure_ascii=False, indent=2)
        handle.flush()
        os.fsync(handle.fileno())
        temp_path = Path(handle.name)
    temp_path.replace(path)


def parse_version(value: str | None) -> tuple:
    if not value:
        return tuple()
    parts = []
    for piece in str(value).strip().replace("-", ".").split("."):
        if piece.isdigit():
            parts.append(int(piece))
        else:
            parts.append(piece)
    return tuple(parts)


def local_appdata() -> Path:
    raw = os.environ.get("LOCALAPPDATA") or os.environ.get("APPDATA")
    if not raw:
        raise RuntimeError("LOCALAPPDATA / APPDATA not available")
    return Path(raw)


def state_root(settings: LauncherSettings) -> Path:
    return local_appdata() / settings.app_id


def ensure_state_tree(root: Path) -> None:
    for relative in APP_STATE_SUBDIRS:
        (root / relative).mkdir(parents=True, exist_ok=True)


def _same_target(link_path: Path, target_path: Path) -> bool:
    try:
        return link_path.resolve() == target_path.resolve()
    except Exception:
        return False


def ensure_dir_link(link_path: Path, target_path: Path, allow_replace_dir: bool = False) -> None:
    target_path.mkdir(parents=True, exist_ok=True)
    link_path.parent.mkdir(parents=True, exist_ok=True)

    if link_path.exists() or link_path.is_symlink():
        if _same_target(link_path, target_path):
            return
        if link_path.is_symlink():
            link_path.unlink()
        elif link_path.is_dir():
            if allow_replace_dir:
                shutil.rmtree(link_path)
            else:
                try:
                    next(link_path.iterdir())
                    raise RuntimeError(f"Refusing to replace non-empty directory: {link_path}")
                except StopIteration:
                    link_path.rmdir()
        else:
            link_path.unlink()

    if os.name == "nt":
        subprocess.run(
            ["cmd", "/c", "mklink", "/J", str(link_path), str(target_path)],
            check=True,
            capture_output=True,
            text=True,
        )
    else:
        os.symlink(str(target_path), str(link_path), target_is_directory=True)


def sync_seed_directory(source_dir: Path, target_dir: Path) -> None:
    if not source_dir.exists():
        return
    try:
        if source_dir.resolve() == target_dir.resolve():
            return
    except Exception:
        pass
    target_dir.mkdir(parents=True, exist_ok=True)
    shutil.copytree(source_dir, target_dir, dirs_exist_ok=True)


def prepare_bundle_runtime_layout(bundle_root: Path, state_dir: Path) -> None:
    app_dir = bundle_root / "app"
    kb_seed_dir = app_dir / "kb"
    kb_target_dir = state_dir / "kb"
    sync_seed_directory(kb_seed_dir, kb_target_dir)

    mappings = {
        "input": (state_dir / "input", False),
        "output": (state_dir / "output", False),
        "runs": (state_dir / "runs", False),
        "archive": (state_dir / "archive", False),
        "kb": (kb_target_dir, True),
    }
    for name, (target, allow_replace_dir) in mappings.items():
        ensure_dir_link(app_dir / name, target, allow_replace_dir=allow_replace_dir)


def configure_logging(root: Path) -> Path:
    log_dir = root / "logs" / "launcher"
    log_dir.mkdir(parents=True, exist_ok=True)
    log_path = log_dir / "launcher.log"
    logging.basicConfig(
        level=logging.INFO,
        format="%(asctime)s [%(levelname)s] %(message)s",
        handlers=[
            logging.FileHandler(log_path, encoding="utf-8"),
            logging.StreamHandler(sys.stdout),
        ],
    )
    return log_path


def load_settings(root: Path) -> LauncherSettings:
    settings_path = root / "launcher.json"
    payload = read_json(settings_path, {})
    return LauncherSettings(
        app_id=payload.get("app_id", "AndrogenRAG"),
        display_name=payload.get("display_name", "Androgen RAG"),
        channel=payload.get("channel", "stable"),
        bootstrap_version=payload.get("bootstrap_version", "1.0.0"),
        manifest_url=payload.get("manifest_url", ""),
        update_enabled=bool(payload.get("update_enabled", True)),
        open_browser=bool(payload.get("open_browser", True)),
        default_port=int(payload.get("default_port", 8501)),
        request_timeout_seconds=int(payload.get("request_timeout_seconds", 10)),
    )


def current_state_path(root: Path) -> Path:
    return root / "current.json"


def load_current_state(root: Path) -> dict:
    default_state = {
        "channel": "stable",
        "current_version": None,
        "previous_version": None,
        "last_known_good_version": None,
        "pending_version": None,
        "updated_at": None,
    }
    path = current_state_path(root)
    if not path.exists():
        write_json_atomic(path, default_state)
        return dict(default_state)
    merged = dict(default_state)
    merged.update(read_json(path, default_state))
    return merged


def save_current_state(root: Path, payload: dict) -> None:
    payload = dict(payload)
    payload["updated_at"] = utc_now_iso()
    write_json_atomic(current_state_path(root), payload)


def versions_root(root: Path) -> Path:
    return root / "packages" / "versions"


def version_dir(root: Path, version: str) -> Path:
    return versions_root(root) / version


def required_bundle_paths(bundle_root: Path) -> list[Path]:
    return [
        bundle_root / "runtime" / "python.exe",
        bundle_root / "app" / "scripts" / "streamlit_app.py",
    ]


def bundle_is_valid(bundle_root: Path) -> bool:
    return all(path.exists() for path in required_bundle_paths(bundle_root))


def discover_bootstrap_dir(root: Path, requested_version: str) -> Path | None:
    candidate = root / "bootstrap" / requested_version
    if candidate.exists():
        return candidate
    bootstrap_root = root / "bootstrap"
    if not bootstrap_root.exists():
        return None
    dirs = [path for path in bootstrap_root.iterdir() if path.is_dir()]
    if not dirs:
        return None
    dirs.sort(key=lambda item: parse_version(item.name), reverse=True)
    return dirs[0]


def ensure_bootstrap_available(install_dir: Path, state_dir: Path, current_state: dict, bootstrap_version: str) -> dict:
    if current_state.get("current_version"):
        existing = version_dir(state_dir, current_state["current_version"])
        if bundle_is_valid(existing):
            return current_state

    source = discover_bootstrap_dir(install_dir, bootstrap_version)
    if source is None:
        raise RuntimeError("No bootstrap bundle available. Reinstall required.")

    target = version_dir(state_dir, source.name)
    if not target.exists():
        logging.info("Bootstrapping version %s from %s", source.name, source)
        shutil.copytree(source, target)

    current_state["current_version"] = source.name
    current_state["last_known_good_version"] = source.name
    current_state["pending_version"] = None
    return current_state


def fetch_remote_manifest(url: str, timeout_seconds: int) -> dict | None:
    if not url:
        return None
    request = urllib.request.Request(url, headers={"User-Agent": "androgen-rag-launcher/1.0"})
    try:
        with urllib.request.urlopen(request, timeout=timeout_seconds) as response:
            return json.loads(response.read().decode("utf-8"))
    except urllib.error.URLError as exc:
        logging.warning("Manifest fetch failed: %s", exc)
        return None


def download_file(url: str, target_path: Path, timeout_seconds: int) -> None:
    target_path.parent.mkdir(parents=True, exist_ok=True)
    request = urllib.request.Request(url, headers={"User-Agent": "androgen-rag-launcher/1.0"})
    with urllib.request.urlopen(request, timeout=timeout_seconds) as response, target_path.open("wb") as handle:
        shutil.copyfileobj(response, handle)


def sha256_of(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        while True:
            chunk = handle.read(1024 * 1024)
            if not chunk:
                break
            digest.update(chunk)
    return digest.hexdigest()


def normalize_extracted_root(temp_root: Path) -> Path:
    children = [path for path in temp_root.iterdir()]
    if len(children) == 1 and children[0].is_dir():
        return children[0]
    return temp_root


def install_zip_bundle(zip_path: Path, target_dir: Path) -> None:
    target_dir.parent.mkdir(parents=True, exist_ok=True)
    temp_extract_root = Path(tempfile.mkdtemp(prefix="bundle_extract_", dir=str(target_dir.parent)))
    try:
        with zipfile.ZipFile(zip_path, "r") as archive:
            archive.extractall(temp_extract_root)
        extracted_root = normalize_extracted_root(temp_extract_root)
        if not bundle_is_valid(extracted_root):
            raise RuntimeError(f"Bundle missing required files: {extracted_root}")
        temp_target = Path(tempfile.mkdtemp(prefix="bundle_stage_", dir=str(target_dir.parent)))
        shutil.rmtree(temp_target)
        shutil.move(str(extracted_root), str(temp_target))
        if target_dir.exists():
            shutil.rmtree(target_dir)
        temp_target.replace(target_dir)
    finally:
        shutil.rmtree(temp_extract_root, ignore_errors=True)


def maybe_update_bundle(settings: LauncherSettings, state_dir: Path, current_state: dict) -> dict:
    if not settings.update_enabled:
        return current_state

    manifest = fetch_remote_manifest(settings.manifest_url, settings.request_timeout_seconds)
    if not manifest:
        return current_state

    remote_version = manifest.get("version")
    current_version = current_state.get("current_version")
    if parse_version(remote_version) <= parse_version(current_version):
        return current_state

    windows = manifest.get("windows") or {}
    url = windows.get("url")
    sha256 = (windows.get("sha256") or "").lower()
    expected_size = int(windows.get("size") or 0)
    min_launcher_version = manifest.get("min_launcher_version")
    launcher_version = "1.0.0"

    if parse_version(min_launcher_version) > parse_version(launcher_version):
        logging.warning("Remote bundle requires launcher %s; current launcher is %s", min_launcher_version, launcher_version)
        return current_state

    if not url or not remote_version:
        logging.warning("Manifest is missing url/version; skip update")
        return current_state

    download_dir = state_dir / "cache" / "downloads"
    download_path = download_dir / f"{remote_version}.zip.partial"
    final_download_path = download_dir / f"{remote_version}.zip"
    logging.info("Downloading bundle %s from %s", remote_version, url)
    download_file(url, download_path, settings.request_timeout_seconds)

    if expected_size and download_path.stat().st_size != expected_size:
        raise RuntimeError(f"Downloaded size mismatch for {remote_version}")

    if sha256 and sha256_of(download_path).lower() != sha256:
        raise RuntimeError(f"SHA256 mismatch for {remote_version}")

    download_path.replace(final_download_path)
    target = version_dir(state_dir, remote_version)
    install_zip_bundle(final_download_path, target)

    updated = dict(current_state)
    updated["pending_version"] = remote_version
    save_current_state(state_dir, updated)
    logging.info("Installed pending version %s", remote_version)
    return updated


def find_free_port(preferred_port: int) -> int:
    for port in [preferred_port, 8502, 8503, 8504, 8505]:
        with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
            sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
            if sock.connect_ex(("127.0.0.1", port)) != 0:
                return port
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
        sock.bind(("127.0.0.1", 0))
        return int(sock.getsockname()[1])


def build_runtime_env(bundle_root: Path, install_dir: Path, state_dir: Path, port: int, version: str) -> dict:
    runtime_dir = bundle_root / "runtime"
    app_dir = bundle_root / "app"
    env = os.environ.copy()
    env["PYTHONUTF8"] = "1"
    env["PYTHONIOENCODING"] = "utf-8"
    env["APP_INSTALL_DIR"] = str(install_dir)
    env["APP_STATE_DIR"] = str(state_dir)
    env["APP_DATA_DIR"] = str(state_dir)
    env["APP_BUNDLE_DIR"] = str(bundle_root)
    env["APP_BUNDLE_APP_DIR"] = str(app_dir)
    env["APP_VERSION"] = version
    env["APP_PORT"] = str(port)
    temp_dir = state_dir / "cache" / "temp"
    temp_dir.mkdir(parents=True, exist_ok=True)
    env["APP_TEMP_DIR"] = str(temp_dir)
    env["TEMP"] = str(temp_dir)
    env["TMP"] = str(temp_dir)

    path_entries = [
        runtime_dir,
        runtime_dir / "Scripts",
        runtime_dir / "Library" / "bin",
        runtime_dir / "Library" / "mingw-w64" / "bin",
        runtime_dir / "Library" / "usr" / "bin",
        runtime_dir / "DLLs",
    ]
    existing_path = env.get("PATH", "")
    env["PATH"] = os.pathsep.join([str(item) for item in path_entries if item.exists()] + ([existing_path] if existing_path else []))

    chemprop = runtime_dir / "Scripts" / "chemprop.exe"
    admet_predict = runtime_dir / "Scripts" / "admet_predict.exe"
    sidecar_chemprop = bundle_root / "vendor" / "envs" / "pk_env" / "Scripts" / "chemprop.exe"
    fpgnn_repo = bundle_root / "vendor" / "fpgnn"
    sidecar_fpgnn_python = bundle_root / "vendor" / "envs" / "fpgnn_cyp2" / "python.exe"
    smartcyp_jar = bundle_root / "vendor" / "smartcyp" / "target" / "smartcyp.jar"
    bundled_java = bundle_root / "vendor" / "jre" / "bin" / "java.exe"
    sidecar_sygma_python = bundle_root / "vendor" / "envs" / "sygma_env" / "python.exe"

    if sidecar_chemprop.exists():
        env["CHEMPROP_EXE"] = str(sidecar_chemprop)
    elif chemprop.exists():
        env["CHEMPROP_EXE"] = str(chemprop)

    if admet_predict.exists():
        env["ADMET_PREDICT_EXE"] = str(admet_predict)

    if fpgnn_repo.exists():
        env["FPGNN_REPO"] = str(fpgnn_repo)
        env["FPGNN_PYTHON"] = str(sidecar_fpgnn_python if sidecar_fpgnn_python.exists() else (runtime_dir / "python.exe"))
    if smartcyp_jar.exists():
        env["SMARTCYP_JAR"] = str(smartcyp_jar)
    if bundled_java.exists():
        env["SMARTCYP_JAVA"] = str(bundled_java)

    env["SYGMA_PYTHON"] = str(sidecar_sygma_python if sidecar_sygma_python.exists() else (runtime_dir / "python.exe"))
    return env


def streamlit_command(bundle_root: Path, port: int) -> list[str]:
    python_exe = bundle_root / "runtime" / "python.exe"
    app_script = bundle_root / "app" / "scripts" / "streamlit_app.py"
    return [
        str(python_exe),
        "-m",
        "streamlit",
        "run",
        str(app_script),
        "--server.address",
        "127.0.0.1",
        "--server.port",
        str(port),
        "--server.headless",
        "true",
        "--browser.gatherUsageStats",
        "false",
    ]


def launch_app(bundle_root: Path, install_dir: Path, state_dir: Path, version: str, port: int) -> subprocess.Popen:
    log_dir = state_dir / "logs" / "app"
    log_dir.mkdir(parents=True, exist_ok=True)
    stdout_path = log_dir / f"app-{version}-stdout.log"
    stderr_path = log_dir / f"app-{version}-stderr.log"
    stdout_handle = stdout_path.open("a", encoding="utf-8")
    stderr_handle = stderr_path.open("a", encoding="utf-8")

    env = build_runtime_env(bundle_root, install_dir, state_dir, port, version)
    creationflags = 0
    if os.name == "nt":
        creationflags = subprocess.CREATE_NEW_PROCESS_GROUP

    process = subprocess.Popen(
        streamlit_command(bundle_root, port),
        cwd=str(bundle_root / "app"),
        env=env,
        stdout=stdout_handle,
        stderr=stderr_handle,
        creationflags=creationflags,
    )
    return process


def wait_for_ready(port: int, timeout_seconds: int) -> bool:
    start = time.time()
    health_urls = [
        f"http://127.0.0.1:{port}/_stcore/health",
        f"http://127.0.0.1:{port}/",
    ]
    while time.time() - start < timeout_seconds:
        for url in health_urls:
            try:
                with urllib.request.urlopen(url, timeout=3) as response:
                    if response.status < 500:
                        return True
            except Exception:
                pass
        time.sleep(1)
    return False


def open_app_browser(port: int) -> None:
    webbrowser.open(f"http://127.0.0.1:{port}/", new=1)


def commit_successful_launch(state_dir: Path, current_state: dict, launched_version: str) -> dict:
    next_state = dict(current_state)
    current_version = current_state.get("current_version")
    if current_version != launched_version:
        next_state["previous_version"] = current_version
    next_state["current_version"] = launched_version
    next_state["last_known_good_version"] = launched_version
    next_state["pending_version"] = None
    save_current_state(state_dir, next_state)
    return next_state


def rollback_pending(state_dir: Path, current_state: dict) -> dict:
    next_state = dict(current_state)
    next_state["pending_version"] = None
    save_current_state(state_dir, next_state)
    return next_state


def choose_launch_version(current_state: dict) -> str:
    return current_state.get("pending_version") or current_state.get("current_version")


def main() -> int:
    install_dir = install_root()
    settings = load_settings(install_dir)
    state_dir = state_root(settings)
    ensure_state_tree(state_dir)
    configure_logging(state_dir)

    logging.info("Launcher starting from %s", install_dir)
    current_state = load_current_state(state_dir)
    current_state = ensure_bootstrap_available(install_dir, state_dir, current_state, settings.bootstrap_version)
    save_current_state(state_dir, current_state)

    try:
        current_state = maybe_update_bundle(settings, state_dir, current_state)
    except Exception as exc:
        logging.exception("Update failed: %s", exc)

    launch_version = choose_launch_version(current_state)
    if not launch_version:
        raise RuntimeError("No runnable version available")

    preferred = version_dir(state_dir, launch_version)
    prepare_bundle_runtime_layout(preferred, state_dir)
    if not bundle_is_valid(preferred):
        raise RuntimeError(f"Bundle is incomplete: {preferred}")

    port = find_free_port(settings.default_port)
    process = launch_app(preferred, install_dir, state_dir, launch_version, port)

    if wait_for_ready(port, 45):
        commit_successful_launch(state_dir, current_state, launch_version)
        if settings.open_browser:
            open_app_browser(port)
        logging.info("Launched version %s on port %s", launch_version, port)
        return 0

    logging.error("Launch failed for version %s", launch_version)
    try:
        process.terminate()
    except Exception:
        pass

    pending_version = current_state.get("pending_version")
    fallback_version = current_state.get("current_version")
    if pending_version and fallback_version and pending_version != fallback_version:
        logging.warning("Rolling back to %s", fallback_version)
        current_state = rollback_pending(state_dir, current_state)
        fallback_bundle = version_dir(state_dir, fallback_version)
        prepare_bundle_runtime_layout(fallback_bundle, state_dir)
        if bundle_is_valid(fallback_bundle):
            fallback_port = find_free_port(settings.default_port)
            fallback_process = launch_app(fallback_bundle, install_dir, state_dir, fallback_version, fallback_port)
            if wait_for_ready(fallback_port, 45):
                commit_successful_launch(state_dir, current_state, fallback_version)
                if settings.open_browser:
                    open_app_browser(fallback_port)
                logging.info("Rollback launch succeeded on %s", fallback_version)
                return 0
            try:
                fallback_process.terminate()
            except Exception:
                pass

    return 1


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:
        message = f"Launcher fatal error: {exc}"
        print(message, file=sys.stderr)
        raise



