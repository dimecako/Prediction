import hmac
import http.client
import os
import subprocess
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer


class Gateway(BaseHTTPRequestHandler):
    def do_GET(self):
        if self.path != "/health":
            self.send_error(404)
            return
        self.forward("GET", "/", None)

    def do_POST(self):
        supplied = self.headers.get("X-FlareSolverr-Key", "").encode()
        if not hmac.compare_digest(supplied, API_KEY):
            self.send_error(401, "Authentication required")
            return
        if self.path != "/v1":
            self.send_error(404)
            return
        try:
            length = int(self.headers.get("Content-Length", "0"))
        except ValueError:
            self.send_error(400, "Invalid Content-Length")
            return
        if not 0 < length <= 65536:
            self.send_error(413, "Request body must be between 1 and 65536 bytes")
            return
        self.connection.settimeout(30)
        body = self.rfile.read(length)
        if len(body) != length:
            self.send_error(400, "Incomplete request body")
            return
        self.forward("POST", "/v1", body)

    def forward(self, method, path, body):
        connection = http.client.HTTPConnection("127.0.0.1", 8191, timeout=210)
        try:
            connection.request(method, path, body, {"Content-Type": "application/json"})
            response = connection.getresponse()
            payload = response.read()
        except (OSError, http.client.HTTPException) as error:
            print(f"[Gateway] Backend request failed: {error}", flush=True)
            self.send_error(502, "FlareSolverr backend unavailable")
            return
        finally:
            connection.close()
        self.send_response(response.status)
        self.send_header("Content-Type", response.getheader("Content-Type", "application/json"))
        self.send_header("Content-Length", str(len(payload)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(payload)


if __name__ == "__main__":
    API_KEY = os.environ.get("FLARESOLVERR_API_KEY", "").encode()
    if len(API_KEY) < 32:
        raise RuntimeError("FLARESOLVERR_API_KEY must contain at least 32 bytes")
    port = int(os.environ.get("PORT", "10000"))
    if port == 8191:
        raise RuntimeError("Gateway PORT must differ from backend port 8191")
    backend_env = dict(os.environ, HOST="127.0.0.1", PORT="8191")
    backend = subprocess.Popen(
        ["/usr/local/bin/python", "-u", "/app/flaresolverr.py"], env=backend_env
    )
    server = ThreadingHTTPServer(("0.0.0.0", port), Gateway)

    def watch_backend():
        code = backend.wait()
        print(f"[Gateway] FlareSolverr exited with code {code}", flush=True)
        server.shutdown()

    threading.Thread(target=watch_backend, daemon=True).start()
    try:
        server.serve_forever()
    finally:
        server.server_close()
        if backend.poll() is None:
            backend.terminate()
        backend.wait()
    raise SystemExit(1)
