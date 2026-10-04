# Continuity audit — gate C2

Gate C2 of the [1.0 acceptance criteria](../blind-evaluation/ACCEPTANCE.md): nobody is given somebody
else's name. It runs the frozen engine the way `silueta evaluate` does for C1: one vault across the
corpus, the default lineage, the evaluator's document order. Invented names are drawn at random, so two
runs differ in which names they drew and in nothing a score sees. A test holds the two to the same scores.

- **Continuity, automatic.** After each record, every roster subject must still have one invented name,
  and none may have been retired. Exit code `1` when it breaks.
- **Attribution, confirmed by a person.** Every replacement the engine gave a person's invented name is
  listed with the gold spans it overlaps. Flags direct attention, and none of them is a failure:
  - `kind-differs`: the gold's kind is not the roster's;
  - `no-gold-span`: an invented name landed on text the gold says identifies nobody;
  - `several-gold-spans`: the replacement covers more than one gold span.

  The gold does not say *whose* name a span is, and deciding it from the text would be a second matcher.
  C2 passes only when the assessor has confirmed the whole list.

## Run it on the frozen engine

The audit refuses to run on any other Core than the one you name, and writes nothing if the hash differs.
Build the tool, then copy the Core assembly out of the frozen CLI package over the tool's own:

```powershell
dotnet publish tools/Silueta.ContinuityAudit -c Release -o <tool-dir>
python -c "import zipfile,sys; open(sys.argv[2],'wb').write(zipfile.ZipFile(sys.argv[1]).read('tools/net10.0/any/Silueta.Core.dll'))" <study>/engine-cli.nupkg <tool-dir>/Silueta.Core.dll
dotnet <tool-dir>/Silueta.ContinuityAudit.dll --gold <adjudicated gold dir> --core-sha256 <coreAssemblySha256 from freeze.json> --out <report.json> --assessor-sheet <private sheet.md>
```

This works because the frozen assembly has the same identity (`Silueta.Core` 0.3.0.0) as the one the tool
was built against. Two constraints follow:

- the tool may use only Core APIs that exist at the frozen commit;
- the audit must be run before the assembly version is bumped, or with the tool built from a commit before
  that bump.

## The operator packet

`--operator-packet <new dir>` writes, from the same invocation, what the pilot's operator works from. It
holds each input with its roster and the engine's output, plus a residue warning where the CLI would have
refused to write. It also holds `final/<id>.txt`, a copy of each output for the operator to correct, and
`engine-output/` as the untouched reference. Because both come from one invocation, the operator corrects
exactly the outputs whose attributions C2 listed. There are no gold labels in it. `times.json` collects the
minutes per document, and `packet.json` the hash of each engine output. The packet is never written over an
existing directory. Gate C3 is then assessed with `workflow.py assess`.

## What each output may do

| Output | Holds | May travel |
| --- | --- | --- |
| `--out` report | offsets, opaque subject ids, kinds, flags, counts, the Core hash | yes, like `evaluate`'s report |
| `--assessor-sheet` | the replaced text in its sentence, the subject's canonical name, the gold quotes | **no**: keep it with the records |
| `--operator-packet` | inputs, rosters and outputs, invented names included | **no**: only to the operator |

The report carries no invented name. A list pairing invented names with subject ids would file the way
back beside the data.
