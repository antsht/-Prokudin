# Prokudin

Prokudin reconstructs faithful RGB images from archival Prokudin-Gorskii grayscale channel scans. Its colour-correction workflow lets an operator neutralize casts while preserving intentional image characteristics.

## Colour correction

**Colour Correction**:
The non-destructive adjustments that rebuild Prokudin's derived RGB result while leaving the aligned R/G/B channel images unchanged.
_Avoid_: Channel retouching, source-image editing

**Full Colour Reset**:
Restores RGB exposure to 0; White-Balance Source to Auto; clears White Pick and restores its radius to 3 px; restores temperature and tint to 0; restores Master Levels to Auto; and neutralizes all Channel Levels.
_Avoid_: Reset Levels

**Master Levels**:
The global black point, white point, and gamma controls that shape overall RGB contrast. Its mode can be Auto, Manual, or Off; Auto derives one luminance curve applied equally to all channels. Manual values persist while Auto or Off is active.
_Avoid_: Global channel levels

**Levels Scope**:
The selected target for level controls: Master, red, green, or blue. A single control set edits the active scope.
_Avoid_: Levels tab, level channel

**Levels Histogram**:
A compact input-distribution graph for the active Levels Scope, with draggable black-point and white-point handles plus a gamma midpoint handle. Master uses luminance; R, G, and B each use their own input channel immediately before that scope's curve.
_Avoid_: Colour histogram, levels graph

**Channel Levels**:
Independent red, green, and blue black point, white point, and gamma controls used to correct colour casts without changing the role of Master Levels. They are always direct manual adjustments with neutral defaults.
_Avoid_: Master levels, global levels

**Channel Exposure**:
Independent red, green, and blue brightness adjustments in exposure stops, used for quick broad correction before precise Channel Levels adjustments.
_Avoid_: Channel Levels

**White-Balance Source**:
The mutually exclusive starting method for neutralizing a colour cast: Off/manual, Auto, or White Pick. It defaults to Auto for new projects and full colour resets. Temperature and tint are optional adjustments applied after this source.
_Avoid_: White-balance mode, colour-temperature mode

**White Pick**:
An operator-selected neutral image patch used as the persistent White-Balance Source, sampled with an adjustable-radius area around the chosen point. Selecting White Pick immediately enters picking mode; a later pick replaces the sample. Without a committed sample, White Pick applies no base correction and prompts the operator to select a neutral area. The preview shows the sampling circle while picking and a marker for the committed sample. A sample persists while another source is active, until replaced or removed by a full colour reset.
_Avoid_: Pipette white balance, eyedropper white balance

**White Pick Radius**:
The operator-controlled radius that defines the White Pick sampling patch. It ranges from 1 to 25 px, defaults to 3 px, and immediately resamples an existing White Pick when changed.
_Avoid_: Fixed pipette radius

**White Pick Quality Warning**:
A non-blocking inline indication that a White Pick sample may be unreliable because it is too dark, highly textured, or strongly coloured. It states the reason and provides a Use anyway action.
_Avoid_: Invalid white pick, rejected white pick

**Temperature and Tint**:
Optional manual colour-balance adjustments applied after the White-Balance Source; temperature shifts warm versus cool, and tint shifts green versus magenta. Each ranges from -100 to +100 and defaults to 0.
_Avoid_: White-Balance Source
