Just as we moved away from state machine I realized in order to make Behavior tree smart... you need more information and actual planning. 

To be fair, I already had a basic working behavior tree to work with just... it was so fucking stupid. 

So we will be implementing additional systems alongside it, the idea is to create a 4 layer AI structure:

1. The Director - His role is Pacing and Tension control.
	- Basically the idea is to create a movie director, a cheating layer of ai that manipulates the decision-making by changing data in the hunters memory. (Additionally he will have access to causing events but its not a part of the HunterAI layer)
	- **Input**: Game Tension, Game Time
	- **Output**: High level goal (searching zones and moving hunter around in order to keep the loosely horror movie tension theory)
2. The Brain - Strategist, Utility system and HTN (planning jobs)
	- Utility - a layer that keeps the moods of HunterAI, basically when hunter will patrol or see some events he will keep track of his inner variables such as aggression, caution, frustration etc. etc.
		- It will decide how he currently moves, is he stealthy or more erratic based on mood he is currently in. 
		- Additionally it will create goals for HTN to make, like patrolling some rooms in specific ways. 
	- HTN (Planner) - Annoyingly, behavior tree patrolling is stupid af, and predictable. So HTN is required in order to create a job queue. What jobs to do like going to vantage points to see some object or searching through some other objects. 
		- Job Queue will basically be list of jobs and re-validating conditions. Like 
			-    Find Door. Add Job: Peek(Door)
				Find Hiding Spots: Interact(Locker)
				Find Hiding Spots: Vantage(Table)
				Peek(Door) > Interact(Locker) > Vantage(Table)'
	- **Input:** Optional goals from director, local stimuli (noise/events)
	- **Output:** A queue of `HunterJobs` (e.g., `[Peek Door]`, `[Check Table]`, `[Wait]`).
3. The Body (behavior tree) 
	- Reactive decision making, executing smallest decisions for the current job since its good at doing that... lets not make behavior tree do the thinking. It handles the moment-to-moment logic: "Am I at the destination? Did I see the player? Is the path blocked?"
	- **Crucial Change:** It no longer decides _where_ to go. It just asks the Brain: "What is my next task?" and does it.