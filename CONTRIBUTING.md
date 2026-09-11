# Contributing

Thank you — genuinely. A de-identifier gets better the way a dictionary does: one specific, boring,
real case at a time.

## The three rules

1. **Never commit real data.** No transcripts, no rosters, no vaults, not "anonymised" ones. Invent the
   example; the library is deterministic, so an invented case reproduces exactly.
2. **Extension points are JSON, not code.** Pattern rules and, soon, language packs live in JSON so that
   adding a rule or a language is adding a file. If your change makes someone edit C# to add a phone
   format, the change is in the wrong place.
3. **A detection change comes with its evidence.** New rule, loosened threshold, extra gazetteer: say
   what it catches that was missed before, and what it now removes that it should not. "It felt better"
   is not a measurement, and this library's entire argument is that it measures itself.

## What is most wanted

- **Cases the phonetic key gets wrong.** A name your recogniser mangles in a way `PhoneticKey` cannot
  follow. One line in `PhoneticKeyTests`, and the rule that would fix it — with what that rule costs
  elsewhere. Every rule in there buys one language and taxes another; see `Docs/ALGORITHM.md`.
- **Pattern rules for identifiers with a shape** in your country: national ids, phone formats, record
  numbers, postal codes.
- **Gold-set tooling.** The measurement is the point, and the annotation side is the thinnest part.

## Running it

```bash
dotnet build Silueta.slnx
dotnet test Silueta.slnx
dotnet run --project src/Silueta.Cli -- demo
```

CI runs the tests, then checks two things you can check locally: that the demo leaks nothing from its
own roster, and that the README still prints what it claims. If you change the surrogate list or the
policy seed, the README block changes with it.

## Style

Comments explain **why**, not what. The code says what it does; the comment is for the decision that is
not obvious six months later — which trade-off was taken, and what it cost.
