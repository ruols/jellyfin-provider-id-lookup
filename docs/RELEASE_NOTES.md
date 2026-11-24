Experimental candidate for Jellyfin 12.2.0 / net10.0, pinned to nyanmisaka/jellyfin:261006-amd64-testing.

Uses native ILibraryManager.GetItemIds / InternalItemsQuery.HasAnyProviderIds. No independent index, cache, media scan, timer or playback proxy.

Build and automated tests must pass before creating this draft. Real server loading, restricted-user permissions, movie/series/season/episode lookups and EPlayerX aggregation still require acceptance. Keep the current Go proxy and port mappings unchanged until those checks pass.

A draft or a private GitHub release is not an anonymously accessible Jellyfin Web installation source. Publication and manifest activation are separate choices described in README.md.
