Summary of Changes
New variable: private Transform currentPatrolTarget = null; to keep track of the chosen point.
New method: SelectNextPatrolPoint() contains all the logic for choosing which patrol point to visit.
Modified Patrol() method: This method now checks for two conditions:
If the hunter has reached a destination, it updates its internal memory.
If the hunter needs a new destination, it calls SelectNextPatrolPoint().
