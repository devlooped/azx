# Releases skip SponsorLink announcements and sponsor injection

Draft notes copy the Upstream Azure CLI release body. SponsorLink's org webhook posts a published release to X unless the body contains `<!-- !X -->`, and injects a sponsors section unless it contains `<!-- nosponsors -->`. We always append both: the notes are Azure's work, not ours. `<!-- X -->` is the opposite (force-announce) and must not appear.
