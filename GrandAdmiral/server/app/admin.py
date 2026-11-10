"""Admin commands, run inside the API pod (it holds the server key):

    kubectl -n grand-admiral exec deploy/grand-admiral-api -- python -m app.admin selfplay-credentials

Creates a fixed install for the in-cluster self-play runner and prints its credentials as the JSON
file the game reads with -gaCredentials. Put that in the `grand-admiral-selfplay` Secret and its
install_id in GA_SELFPLAY_INSTALLS (API and trainer). Running it again prints the same identity.
"""

import argparse
import json
import uuid

from sqlalchemy import select

from osiris_ml import storage

from . import security
from .main import Settings


def selfplay_credentials(settings, name="selfplay"):
    install_id = str(uuid.UUID(bytes=security._mac(settings.server_key, "selfplay", name)[:16], version=4))
    engine = storage.engine_for(settings.db_url)
    with engine.begin() as conn:
        if conn.execute(select(storage.installs.c.id).where(storage.installs.c.id == install_id)).first() is None:
            conn.execute(storage.installs.insert().values(id=install_id, created_at=storage.now(),
                                                          network="selfplay", blocked=False))
    return {"install_id": install_id, "token": security.token_for(settings.server_key, install_id), "server": ""}


def main(argv=None):
    parser = argparse.ArgumentParser(prog="python -m app.admin")
    sub = parser.add_subparsers(dest="command", required=True)
    p = sub.add_parser("selfplay-credentials")
    p.add_argument("--name", default="selfplay")
    args = parser.parse_args(argv)
    print(json.dumps(selfplay_credentials(Settings.from_env(), args.name)))


if __name__ == "__main__":
    main()
