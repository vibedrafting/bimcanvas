import argparse
import base64
import json
import os
import sys
import time
from datetime import datetime
from urllib import error, request


def post_json(url: str, payload: dict, timeout: int) -> dict:
    data = json.dumps(payload).encode("utf-8")
    req = request.Request(url, data=data, headers={"Content-Type": "application/json"})
    try:
        with request.urlopen(req, timeout=timeout) as resp:
            body = resp.read().decode("utf-8")
            return json.loads(body)
    except error.HTTPError as exc:
        body = exc.read().decode("utf-8") if exc.fp else ""
        raise RuntimeError(f"HTTP {exc.code}: {body}") from exc
    except error.URLError as exc:
        raise RuntimeError(f"Request failed: {exc}") from exc


def save_image(image_data: str, output_path: str) -> None:
    if not image_data:
        raise ValueError("imageData is empty")
    prefix = "data:image/png;base64,"
    if image_data.startswith(prefix):
        image_data = image_data[len(prefix):]
    raw = base64.b64decode(image_data)
    with open(output_path, "wb") as f:
        f.write(raw)


def build_tests(project_path: str) -> list[dict]:
    return [
        {
            "name": "full_user_autofit",
            "payload": {
                "projectPath": project_path,
                "layerPreset": "User",
                "viewport": {"mode": "full"},
                "autoFitViewport": True,
                "scale": 2,
            },
        },
        {
            "name": "room_rz_1_labels_zones_autofit",
            "payload": {
                "projectPath": project_path,
                "layerPreset": "User",
                "layerEnable": ["Labels", "Zones"],
                "viewport": {"mode": "room", "roomId": "rz_1"},
                "autoFitViewport": True,
                "scale": 2,
            },
        },
        {
            "name": "room_r_3_fixed_16_9",
            "payload": {
                "projectPath": project_path,
                "layerPreset": "User",
                "viewport": {"mode": "room", "roomId": "r_3"},
                "autoFitViewport": False,
                "scale": 2,
            },
        },
        {
            "name": "bounds_grid_architecture",
            "payload": {
                "projectPath": project_path,
                "layerPreset": "User",
                "layerEnable": ["Grid", "Architecture"],
                "layerDisable": ["Furniture"],
                "viewport": {
                    "mode": "bounds",
                    "bounds": {"minX": 1000, "minY": 1000, "maxX": 8000, "maxY": 6000},
                },
                "autoFitViewport": True,
                "scale": 2,
            },
        },
        {
            "name": "full_agent_autofit",
            "payload": {
                "projectPath": project_path,
                "layerPreset": "Agent",
                "viewport": {"mode": "full"},
                "autoFitViewport": True,
                "scale": 2,
            },
        },
    ]


def main() -> int:
    parser = argparse.ArgumentParser(description="Background screenshot API tests")
    parser.add_argument(
        "--server",
        default="http://localhost:5000",
        help="Server base URL, e.g. http://localhost:5000",
    )
    parser.add_argument(
        "--project",
        default=r"C:\Users\huhaonan\Documents\BIMCanvas\Projects\demo_1",
        help="Project directory path",
    )
    parser.add_argument(
        "--output",
        default=os.path.join(os.path.dirname(__file__), "output"),
        help="Output directory for images",
    )
    parser.add_argument(
        "--timeout",
        type=int,
        default=90,
        help="Request timeout in seconds",
    )
    parser.add_argument(
        "--only",
        default="",
        help="Comma-separated test names to run (optional)",
    )
    args = parser.parse_args()

    os.makedirs(args.output, exist_ok=True)

    tests = build_tests(args.project)
    if args.only.strip():
        allow = {name.strip() for name in args.only.split(",") if name.strip()}
        tests = [t for t in tests if t["name"] in allow]

    if not tests:
        print("No tests to run.")
        return 1

    api_url = f"{args.server.rstrip('/')}/api/screenshot/render"
    results = []

    for test in tests:
        name = test["name"]
        payload = test["payload"]
        start = time.time()
        try:
            response = post_json(api_url, payload, args.timeout)
            image_data = response.get("imageData")
            timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
            filename = f"{name}_{timestamp}.png"
            output_path = os.path.join(args.output, filename)
            save_image(image_data, output_path)
            elapsed_ms = int((time.time() - start) * 1000)
            results.append((name, "OK", elapsed_ms, output_path))
            print(f"[OK] {name} {elapsed_ms}ms -> {output_path}")
        except Exception as exc:
            elapsed_ms = int((time.time() - start) * 1000)
            results.append((name, "FAIL", elapsed_ms, str(exc)))
            print(f"[FAIL] {name} {elapsed_ms}ms -> {exc}")

    print("\nSummary:")
    for name, status, elapsed_ms, detail in results:
        print(f"- {name}: {status} ({elapsed_ms}ms)")
        if status != "OK":
            print(f"  {detail}")

    return 0 if all(r[1] == "OK" for r in results) else 1


if __name__ == "__main__":
    sys.exit(main())
