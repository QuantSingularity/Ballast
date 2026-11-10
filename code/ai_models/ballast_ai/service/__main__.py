"""Run the service: python -m ballast_ai.service --host 127.0.0.1 --port 8000"""

from __future__ import annotations

import argparse

import uvicorn

from ballast_ai.service.app import create_app


def main() -> None:
    parser = argparse.ArgumentParser(description="Ballast AI inference service")
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=8000)
    parser.add_argument("--log-level", default="warning")
    args = parser.parse_args()
    uvicorn.run(create_app(), host=args.host, port=args.port, log_level=args.log_level)


if __name__ == "__main__":
    main()
