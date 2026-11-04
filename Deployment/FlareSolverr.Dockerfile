FROM ghcr.io/flaresolverr/flaresolverr:v3.5.2
COPY Deployment/flaresolverr_gateway.py /app/flaresolverr_gateway.py
CMD ["/usr/local/bin/python", "-u", "/app/flaresolverr_gateway.py"]
