# Collapsed Quota Progress Design

The user selected changing only the docked, collapsed strip. Hovering still opens the existing rings and details. Use the current ring-window selection introduced in preview.8; across enabled accounts, show the highest-risk displayed window. Keep color and length tied to one selection.

The existing `ShowRemaining` preference controls fill length: 80 percent remaining fills 80 percent; used mode fills the used fraction. Clamp valid values to zero through one; exhausted windows represent zero remaining. Missing, stale, invalid or failed readings show a neutral track with no invented fill. Existing gradient, fixed and threshold color settings remain available.

The chosen approach is to paint a track and clipped fill inside the existing six-by-96-DIP collapsed silhouette. Vertical fills grow bottom to top; horizontal fills grow left to right. This avoids native-window movement and preserves the existing spring. Replacing expanded rings with bars was offered and declined. Add `Appearance.DockShowProgress`, default true for existing and new settings, so the user can restore the previous solid strip.

During expansion, fade the progress overlay into the existing rail background; fully expanded visuals remain unchanged. Rendering stays in WPF drawing geometry, with no per-frame native resize. No account, proxy, hi automation, credential or report-template changes. Documentation uses default black text.
