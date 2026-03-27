# Copilot Instructions

## Project Guidelines
- Prefer server-compatible mechanic: allow granting rested effect without full bed sleep (e.g., shorter threshold, alternate triggers like idle time or a command).
- Use linear falloff for warmth calculation: compute `dist = MathF.Sqrt(distSq); heat = 1f - (dist / maxDistance);` clamp to non-negative; combine using `warmth = MathF.Max(warmth, heat)` (no additive inverse-square accumulation).
- Reduce server cost by avoiding dense block scans and `GetBlockEntity` calls in hot loops; use `HeatSourceRegistry` (chunked registry) and avoid string comparisons in detection. Prefer querying `HeatSourceRegistry.GetWarmthAt` and linear falloff to determine 'nearFire'.
- Simplify rest time delta calculation by removing unused local variables; prefer using motion magnitude squared for idle detection; prefer clearer variable naming for last fire-check timestamp; make warmth threshold configurable. Continue using `HeatSourceRegistry` and linear falloff for fire detection.