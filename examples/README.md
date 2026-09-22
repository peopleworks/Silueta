# Examples

Files to copy, not to admire. Each one loads — a test in this repository runs every file in this folder
through the loader, so an example that stops being valid stops the build rather than wasting your evening.

| File | What it shows |
| --- | --- |
| [`roster.example.json`](roster.example.json) | Who a record is about: the people, and the values a caller already holds. Both ids are required and both must be opaque. |
| [`lineage.clinica-mx.json`](lineage.clinica-mx.json) | A whole lineage in Spanish: labels in the language of the note, the towns the clinic serves (`values`), a policy of its own (`policies`), and pattern rules for a Mexican record number, CURP and phone. |
| [`lineage.clinica-co.json`](lineage.clinica-co.json) | The same for a country the built-in rules know nothing about. Its own word lists (`lists`) — the departments of Colombia, its types of road — named by its own rules, beside the library's `{{month-es}}`. |

```bash
silueta redact --in visita.txt --record r-042 \
               --context examples/roster.example.json \
               --lineage examples/lineage.clinica-mx.json \
               --policy estadistica \
               --out visita.deid.txt --manifest visita.manifest.json --vault vault.json
```

Two commands worth knowing before you write your own:

```bash
silueta lineage > mine.json   # the lineage this build ships with, as a starting point
silueta lists                 # the word lists a rule can name, as {{us-state}} or {{month-es}}
```

**Nothing here is about a real person, a real clinic or a real insurer**, and nothing in this repository
is. That is not a disclaimer: it is the rule the project runs on, and it applies to a test, a corpus, a
demo page and an example alike.
