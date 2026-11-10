# Grand Admiral trainer (nightly CronJob). Build from GrandAdmiral/:  docker build -f ml/Dockerfile .
FROM python:3.12.7-slim-bookworm AS build
ENV PIP_NO_CACHE_DIR=1 PIP_DISABLE_PIP_VERSION_CHECK=1
WORKDIR /src
COPY ml/ ml/
RUN python -m venv /venv && /venv/bin/pip install "./ml[train,postgres]"

FROM python:3.12.7-slim-bookworm
COPY --from=build /venv /venv
RUN useradd --uid 10001 --no-create-home --shell /usr/sbin/nologin ga
USER 10001:10001
ENV PATH=/venv/bin:$PATH PYTHONDONTWRITEBYTECODE=1 PYTHONUNBUFFERED=1 OPENBLAS_NUM_THREADS=2
ENTRYPOINT ["osiris-ml"]
CMD ["round"]
