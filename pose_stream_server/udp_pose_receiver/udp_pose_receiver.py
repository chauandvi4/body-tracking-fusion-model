from __future__ import annotations

import argparse
import asyncio
import json
import logging
from typing import Callable, Iterable, Sequence, Tuple

logger = logging.getLogger(__name__)

class PosePacketProtocol(asyncio.DatagramProtocol):
    def __init__(self, handler: Callable[[dict, Tuple[str, int]], None]) -> None:
        super().__init__()
        self._handler = handler

    def datagram_received(self, data: bytes, addr: Tuple[str, int]) -> None:
        try:
            payload = json.loads(data.decode("utf-8"))
        except (UnicodeDecodeError, json.JSONDecodeError) as exc:
            logger.warning("Failed to decode UDP packet from %s: %s", addr, exc)
            return

        self._handler(payload, addr)


async def run_server(host: str, port: int, on_packet: Callable[[dict, Tuple[str, int]], None]) -> None:
    loop = asyncio.get_running_loop()
    transport, _ = await loop.create_datagram_endpoint(
        lambda: PosePacketProtocol(on_packet), local_addr=(host, port)
    )

    logger.info("Listening for raw UDP pose packets on udp://%s:%d", host, port)
    try:
        await asyncio.Future()
    finally:
        transport.close()


def _pretty_print_packet(packet: dict, addr: Tuple[str, int]) -> None:
    timestamp = packet.get("timestamp")
    hmd = packet.get("hmd", {})
    position = hmd.get("position", {})
    rotation = hmd.get("rotation", {})

    logger.info(
        "Packet from %s timestamp=%s hmd=(%.3f, %.3f, %.3f) yaw=%.1f",
        addr,
        timestamp,
        position.get("x", 0.0),
        position.get("y", 0.0),
        position.get("z", 0.0),
        rotation.get("y", 0.0),
    )

    joint_iterable: Iterable[dict] = packet.get("joints", [])
    joint_list = list(joint_iterable)
    if joint_list:
        joint_summary = ", ".join(
            f"{joint.get('name')}:({joint.get('pose', {}).get('position', {}).get('x', 0.0):.3f},"
            f" {joint.get('pose', {}).get('position', {}).get('y', 0.0):.3f},"
            f" {joint.get('pose', {}).get('position', {}).get('z', 0.0):.3f})"
            for joint in joint_list[:4]
        )
        if len(joint_list) > 4:
            joint_summary += ", ..."
        logger.debug("Sample joints %s", joint_summary)


def parse_args(argv: Sequence[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--host", default="0.0.0.0", help="Interface to bind (default: 0.0.0.0)")
    parser.add_argument("--port", type=int, default=9000, help="UDP port to listen on (default: 9000)")
    parser.add_argument(
        "--verbose",
        action="store_true",
        help="Increase logging verbosity (prints sample joint positions)",
    )
    return parser.parse_args(argv)


def main(argv: Sequence[str] | None = None) -> None:
    args = parse_args(argv)

    logging.basicConfig(level=logging.DEBUG if args.verbose else logging.INFO)

    try:
        asyncio.run(run_server(args.host, args.port, _pretty_print_packet))
    except KeyboardInterrupt:
        logger.info("Shutting down UDP receiver")


if __name__ == "__main__":
    main()