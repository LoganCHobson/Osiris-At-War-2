# Grand Admiral ingest API. Build from GrandAdmiral/:  docker build -f server/Dockerfile .
FROM python:3.12.7-slim-bookworm AS build
ENV PIP_NO_CACHE_DIR=1 PIP_DISABLE_PIP_VERSION_CHECK=1
WORKDIR /src
COPY ml/ ml/
COPY server/ server/
RUN python -m venv /venv && /venv/bin/pip install ./ml ./server

FROM python:3.12.7-slim-bookworm
COPY --from=build /venv /venv
RUN useradd --uid 10001 --no-create-home --shell /usr/sbin/nologin ga
USER 10001:10001
ENV PATH=/venv/bin:$PATH PYTHONDONTWRITEBYTECODE=1 PYTHONUNBUFFERED=1
EXPOSE 8080
# No --proxy-headers: the app reads X-Forwarded-For itself, trusting only the ingress's hop.
CMD ["uvicorn", "app.main:asgi", "--factory", "--host", "0.0.0.0", "--port", "8080", \
     "--no-server-header", "--limit-concurrency", "200", "--timeout-keep-alive", "5", \
     "--h11-max-incomplete-event-size", "16384"]
