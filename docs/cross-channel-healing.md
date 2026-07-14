# Provenance-aware Guided Healing

Status: **implemented** in 0.17.0 (Core `ChannelHealer` / `GuidedHealingEngine`, GUI heal brush and auto-clean apply).

See also: [retouch-provenance ADR](adr/0003-retouch-provenance-for-guided-healing.md), [User Guide](user-guide.md#cross-channel-healing), and [Core API](core-api.md#retouch).

## Goal and boundaries

Guided Healing repairs dust, compact defects, and scratches in one aligned
grayscale channel using evidence from the local target context and its two
aligned siblings. It is reconstruction, not generative restoration:

- only pixels in the supplied healing mask may change;
- guide channels are never modified;
- guide structure may inform a repair, but guide tone is never copied into the
  target channel;
- a large region with insufficient local evidence is left conservative rather
  than completed as invented scene content;
- heal brush and auto-clean run off the Avalonia UI thread.

The selected channel uses the other two channels as guides: `R <- G,B`,
`G <- R,B`, and `B <- R,G`.

## Provenance and guide eligibility

Each prepared R/G/B pixel has one byte of provenance:

| Provenance | Can guide a repair? |
| --- | --- |
| `Original` | Yes |
| `HighConfidenceHealing` | Yes |
| `LowConfidenceHealing` | No |
| `CloneStamp` | No |
| `Unknown` (project predates provenance) | Only when both guides are usable and agree |

Eligibility is checked for every sampled guide pixel, rather than only once for
a component. This prevents a clone-stamped or uncertain guide sample from being
propagated through a neighbouring repair.

Every newly healed masked pixel is marked high- or low-confidence. Clone stamp
marks the stamped mask as `CloneStamp`.

## Repair procedure

1. Find connected components in the reviewed brush or auto-clean mask.
2. Classify each component as compact or scratch from its aspect ratio and
   principal axis.
3. For a compact component, fit a robust local structural relationship from
   unmasked context. The guide contribution is expressed as a local contrast or
   delta, retaining the target's local tone.
4. For a scratch, group pixels along the long axis and split it where transverse
   target tone shows a boundary. Each segment has a separate local repair,
   protecting dark/light and other tonal edges crossed by the scratch.
5. Require per-guide evidence and agreement. When agreement or local context is
   weak, apply a conservative local fallback and mark the result low confidence.
6. For a large component, disable scene-completing prediction. With no suitable
   local evidence, samples remain unchanged rather than synthesizing detail.

The output is rebuilt with the target's original `ImageBuffer` pixel format.
This preserves native 16-bit values instead of routing them through 8-bit
inpaint quantization.

## State lifecycle

The GUI holds one `RetouchProvenanceMap` beside each prepared channel. A map is
deep-cloned in snapshot undo/redo state and restored atomically with its channel.
It is cropped in the same operation as channel/result crop, swapped with its
channel, and reset when a channel is imported or a new alignment creates new
prepared data.

Explicit project saves and autosaves write:

```text
red.provenance.bin
green.provenance.bin
blue.provenance.bin
```

These sidecars are optional for backwards compatibility. A missing or malformed
sidecar loads as an `Unknown` map, which can never act as the sole guide.

## User feedback and diagnostics

The GUI reports a concise status-bar notice when healing had to be conservative
or low-confidence. `Debug heal` continues to write the final healed channel and
debug mask output under `debug/heal/`; it is useful for manual visual validation
of edge and scratch repairs.

## Validation coverage

Focused tests cover:

- exact mask locality and native 16-bit preservation;
- clone and low-confidence guide exclusion, high-confidence eligibility, and
  dual-guide handling of legacy unknown samples;
- lack of speculative completion in unresolved large masks;
- scratch segmentation at a dark/light boundary;
- provenance clone/crop/history/project/autosave round trips;
- crop-to-selection on Result after Guided Healing, including synchronized
  prepared-channel crops.
