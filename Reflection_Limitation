## Reflection

### Tested Improvement

One tested improvement was making the connection between MediaPipe and Unity more stable. During early tests, low-confidence or short-lived gesture results could accidentally trigger actions or leave the elephant in an incorrect state.

The final system accepts only supported gestures above a 0.5 confidence threshold and returns missing gesture data to neutral after 0.5 seconds. The Victory gesture also uses a one-shot trigger, so drinking plays once and cannot be triggered again until the gesture is released. Pose-controlled movement uses smoothing, dead zones, and thresholds to reduce unintended motion.

We also separated forward movement and steering between two performers because shoulder movement could affect hand-height measurements. Testing showed that this division reduced signal interference and made navigation more consistent.

### Remaining Limitation

The system still depends on clear camera visibility and fixed performer positions. Overlapping bodies, crossing camera-zone boundaries, or blocking another performer’s hands or shoulders can reduce tracking stability.

The water-pool interaction is also spatially staged. Victory triggers drinking anywhere in the scene because Unity does not currently check the elephant’s distance from the pool. A future version could add a trigger collider or distance check so drinking is available only near the water.

These results showed us that reliable multi-person interaction depends on both technical filtering and coordinated performance.
