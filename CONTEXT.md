# Prokudin

Prokudin reconstructs faithful RGB images from archival Prokudin-Gorskii grayscale channel scans. Its colour-correction workflow lets an operator neutralize casts while preserving intentional image characteristics.

## Restoration workspace

**Restoration Workspace**:
The single-window editing environment in which an operator progresses from channel import to an exported reconstruction. Its stable structure keeps channels, workflow tools, image preview, and detailed controls available as coordinated parts of one task.
_Avoid_: Wizard, dashboard

**Digital Darkroom**:
The visual character of the Restoration Workspace: quiet, image-led, and precise, with restrained chrome and purposeful controls rather than decorative interface elements.
_Avoid_: Corporate dashboard, skeuomorphic studio

**Neutral Viewing Environment**:
The untextured, non-vintage visual treatment of the Digital Darkroom that lets the source and reconstructed images, rather than interface decoration, convey archival character.
_Avoid_: Faux archival interface, photographic texture

**Accessible Darkroom**:
The Darkroom Design System applied with sufficient contrast, visible keyboard focus, usable hit targets, and non-colour-only state cues across every in-app surface.
_Avoid_: Accessibility mode

**Quiet Motion**:
Short, purpose-led interface transitions that communicate a state change without competing with image work. They honour the operating system's reduced-motion preference.
_Avoid_: Decorative animation

**Command Hierarchy**:
The visual ordering of actions that assigns one primary action to a context, presents supporting actions neutrally, and keeps infrequent configuration actions quiet.
_Avoid_: Equal-weight buttons

**Application Menu**:
The complete, conventional desktop catalog of Prokudin commands. It complements rather than replaces the context command bar used for frequent workflow actions.
_Avoid_: Hamburger navigation, ribbon

**Darkroom Tokens**:
The semantic colour, typography, spacing, radius, elevation, and interaction-state rules that compose the Darkroom Design System across every owned in-app surface.
_Avoid_: Per-view visual values, theme overrides

**System Typography**:
The native platform typeface used with a shared, deliberate type scale and weights throughout the Darkroom Design System, rather than a bundled brand font.
_Avoid_: Display font, per-view font sizing

**Command Icon**:
The consistent outline icon used as shorthand for a frequent, reversible command. Primary, destructive, or otherwise ambiguous commands retain a text label alongside their icon.
_Avoid_: Icon-only primary action, mixed icon families

**Darkroom Dialog**:
The shared in-app dialog composition with a concise purpose, grouped content, and predictable secondary and primary actions in its footer.
_Avoid_: Ad-hoc form window

**Adaptive Workspace**:
The Compact Workspace behaviour that temporarily collapses secondary panels as width becomes constrained, while retaining the operator's persisted panel-visibility choices. The Channel List collapses before the inspector.
_Avoid_: Fixed desktop layout

**Darkroom Theme**:
The default dark appearance of the Digital Darkroom, designed to keep the reconstructed image visually dominant. Light and system-following appearances remain available alternatives.
_Avoid_: Dark mode

**Channel Colour**:
The red, green, or blue visual identifier of an image channel and its channel-specific controls. It never denotes general selection, focus, or command emphasis.
_Avoid_: Accent colour, status colour

**Interaction Accent**:
The contrast-validated cyan-blue colour family that communicates general interactive state in light and Darkroom Theme appearances. It is separate from Channel Colour.
_Avoid_: Channel colour, status colour

**Channel Mark**:
The small abstract motif of three offset RGB plates used beside the Prokudin wordmark on welcome and About surfaces. It is not workspace decoration.
_Avoid_: App logo in the workspace, vintage ornament

**Darkroom Design System**:
The shared visual language of every in-app Prokudin surface, including the Restoration Workspace, welcome screen, settings, export controls, and application dialogs. Native operating-system file dialogs are outside its scope.
_Avoid_: Main-window theme

**Compact Workspace**:
The dense, professional arrangement of the Restoration Workspace that keeps frequent controls close without competing with the image preview or making controls hard to target.
_Avoid_: Spacious workspace, cramped interface

**Workflow Rail**:
The compact icon-led navigation that selects a Restoration Workspace stage. The selected stage is named in the context command bar; each rail item remains discoverable by tooltip and accessible name.
_Avoid_: Workflow toolbar, wizard steps

**Progressive Inspector**:
The inspector arrangement that keeps frequent controls and primary actions immediately visible while advanced, technical, and diagnostic control groups are collapsed until the operator needs them.
_Avoid_: Flat settings form, hidden professional controls

**Precision Control**:
The paired slider and editable numeric value used for a continuous reconstruction setting, allowing both fast adjustment and reproducible exact entry.
_Avoid_: Value-less slider

**Workflow-Justified Addition**:
A new interface capability admitted to the Darkroom Design System only when it removes a demonstrated friction point in the existing reconstruction workflow without adding unrelated image-processing scope.
_Avoid_: Feature-driven redesign

**Channel List**:
The compact channel panel that presents every source channel and the result as a dense, selectable row with a thumbnail, Channel Colour identifier, dimensions, and state.
_Avoid_: Channel-card gallery

**Diagnostic Drawer**:
The collapsible lower panel for detailed processing messages. It is secondary to the concise status and progress information that remains visible during normal work.
_Avoid_: Permanent processing log

**Task Feedback**:
The non-modal operation status shown in the relevant context controls and as a restrained preview overlay. It communicates the active operation, its progress or waiting state, and available actions without preventing workflow changes.
_Avoid_: Blocking progress dialog

**Actionable Message**:
A concise contextual warning or error that states the relevant problem and offers the operator a meaningful next step. Detailed technical information belongs in the Diagnostic Drawer.
_Avoid_: Raw error message, modal error dialog

**Start Reconstruction**:
The primary welcome-screen route that begins a new Restoration Workspace session by importing separate channels or a triptych. Project opening, recent projects, and autosave recovery are secondary routes.
_Avoid_: New project

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
