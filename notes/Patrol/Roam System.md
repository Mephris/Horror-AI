**`MoveToPatrolPoint`** *(Task)Find Target* - Finds the single highest-scoring point using `playerProbability` and memory tags.

**`WanderLocally`** *(Task)Roam* - Runs immediately after arrival at a point, creating a brief, non-pathfinding meander before a new patrol point is chosen.

**`IsAtDestination`** *(Condition)Check Arrival* - Checks if the agent has reached the target and, if so, calls the cleanup method to reset the state.