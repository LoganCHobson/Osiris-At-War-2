baseline.bytes goes here: the signed Grand Admiral v1 model that ships with the game.

    osiris-ml bootstrap --bundle-out Assets/Resources/GrandAdmiral/baseline.bytes

It must be signed by a key listed in Scripts/AI/GrandAdmiral/Core/GATrustedKeys.cs, or the game ignores
it and Grand Admiral plays as Admiral. See GrandAdmiral/README.md at the repo root.
