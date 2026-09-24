# Obstacle and framing screenshots

Captured from TunnelScene with `techno_obstacle_circuit.json` on the offline song clock, so each
frame is pinned to an exact point in the track and is reproducible rather than caught by hand.

Camera as shipped: **y = 1.35, looking level, vertical FOV 60**.

| file | song time | what it shows |
|---|---|---|
| `00_framing_vs_reference.png` | 2.383 s | the shipping framing, for comparison against the competitor reference |
| `01_dodge_wall_45m.png` | 7.876 s | the wall at first sight, 45 m out |
| `02_dodge_wall_12m.png` | 10.626 s | the wall at reading distance, camera still fully at rest |
| `03_curb_10m.png` | 31.705 s | the kerb at reading distance, camera still at rest |
| `04_duck_barrier_10m.png` | 52.276 s | the barrier at reading distance, passage below it clear |
| `05_camera_dodge_right.png` | 11.930 s | wall on the LEFT sweeping past, camera committed right (+1.50), leaned 11 deg into it |
| `06_camera_dodge_left.png` | 15.360 s | wall on the RIGHT sweeping past, camera committed left (-1.50), leaned the other way |
| `07_camera_hop_over_curb.png` | 32.580 s | apex of the hop (+1.15) as the kerb goes under |
| `08_camera_duck_under_barrier.png` | 53.140 s | bottom of the crouch (-0.80), barrier overhead |
| `14_punchhold_held.png` | 42.965 s | a PunchHold STOPPED at the hit point, half consumed from its far end |
| `15_kickhold_held.png` | 22.400 s | the same for a KickHold, on the floor |
| `10..13_extreme_camera_*.png` | 70 s | camera pinned at each extreme, including the roll that goes with a dodge |

`02`, `03` and `04` are with the camera at rest on purpose: every reaction now completes about
0.07 s before the obstacle is level with the camera, so at reading distance nothing has moved yet.

The HUD's combo counter stays dead level in `05` and `06` while the horizon is over by 11 degrees.
That is what a Screen Space Overlay canvas buys, and the measured rects agree to the pixel.

The last four are the ring check: at every extreme the arch legs stand outside the road, nothing
is left rendering behind the camera, and no ring recycles while still visible. `13` is the hard
floor the camera can never go below.
