"""OpenSSH ProxyCommand bridge to the local EasyConnect SOCKS5 endpoint."""
import argparse
import ipaddress
import socket
import struct
import sys
import threading

parser = argparse.ArgumentParser()
parser.add_argument('host')
parser.add_argument('port', type=int)
parser.add_argument('--probe', action='store_true')
parser.add_argument('--timeout', type=float, default=30)
args = parser.parse_args()

def receive(sock, count):
    data = bytearray()
    while len(data) < count:
        chunk = sock.recv(count - len(data))
        if not chunk:
            raise ConnectionError('SOCKS5 connection closed unexpectedly')
        data.extend(chunk)
    return bytes(data)

try:
    stage = 'connect to local campus proxy'
    sock = socket.create_connection(('127.0.0.1', 1080), timeout=args.timeout)
    sock.settimeout(args.timeout)
    stage = 'SOCKS5 greeting'
    sock.sendall(b'\x05\x01\x00')
    if receive(sock, 2) != b'\x05\x00':
        raise ConnectionError('SOCKS5 did not accept unauthenticated local access')
    try:
        packed = ipaddress.ip_address(args.host).packed
        address = bytes([1 if len(packed) == 4 else 4]) + packed
    except ValueError:
        encoded = args.host.encode('idna')
        if len(encoded) > 255:
            raise ValueError('Target hostname is too long')
        address = b'\x03' + bytes([len(encoded)]) + encoded
    stage = f'SOCKS5 connect to {args.host}:{args.port}'
    sock.sendall(b'\x05\x01\x00' + address + struct.pack('!H', args.port))
    version, status, _, kind = receive(sock, 4)
    if version != 5 or status != 0:
        raise ConnectionError(f'Campus SOCKS5 could not connect to {args.host}:{args.port}; status={status}')
    length = {1: 4, 4: 16}.get(kind)
    if length is None:
        if kind != 3:
            raise ConnectionError('Invalid SOCKS5 response address type')
        length = receive(sock, 1)[0]
    receive(sock, length + 2)
    if args.probe:
        print(f'TCP connection established through campus SOCKS5; waiting for service banner.', flush=True)
        stage = 'read SSH service banner'
        banner = sock.recv(256)
        print(f'{args.host}:{args.port} via campus SOCKS5: {banner.decode(errors="replace").strip()}')
        sock.close()
        sys.exit(0)
    sock.settimeout(None)

    def stdin_to_socket():
        try:
            while True:
                data = sys.stdin.buffer.read1(65536)
                if not data:
                    break
                sock.sendall(data)
        except OSError:
            pass
        finally:
            try:
                sock.shutdown(socket.SHUT_WR)
            except OSError:
                pass

    threading.Thread(target=stdin_to_socket, daemon=True).start()
    while True:
        data = sock.recv(65536)
        if not data:
            break
        sys.stdout.buffer.write(data)
        sys.stdout.buffer.flush()
    sock.close()
except (OSError, ValueError, ConnectionError) as error:
    print(f'Campus SSH bridge ({stage}): {error}', file=sys.stderr)
    sys.exit(1)
