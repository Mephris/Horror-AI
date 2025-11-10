


Step 3: Modify the Existing Patrol() Method
Now, simplify your Patrol() method. It no longer needs the foreach loop. Instead, it will check if the hunter needs to pick a new patrol point and, if so, call your new SelectNextPatrolPoint() method.
Here's the modified Patrol() method:

private void Patrol()
{
    // The hunter has just arrived at the last patrol point
    if (isMoving && agent.remainingDistance <= 0.1f)
    {
        isMoving = false;

        // Step 2: Update the hunter's memory for the point it just reached.
        ``if (patrolPointData.ContainsKey(currentPatrolTarget))``
        ``{``
            ``patrolPointData[currentPatrolTarget].lastPatrolTime = Time.time;``
            ``patrolPointData[currentPatrolTarget].playerProbability = 0f; // Reset probability after checking``
        ``}``
    ``}``

    ``// The hunter is not moving and needs a new target``
    ``if (!isMoving)``
    ``{``
        ``SelectNextPatrolPoint();``
    ``}``
}

Używaj kodu z rozwagą.

Summary of Changes
New variable: private Transform currentPatrolTarget = null; to keep track of the chosen point.
New method: SelectNextPatrolPoint() contains all the logic for choosing which patrol point to visit.
Modified Patrol() method: This method now checks for two conditions:
If the hunter has reached a destination, it updates its internal memory.
If the hunter needs a new destination, it calls SelectNextPatrolPoint().
