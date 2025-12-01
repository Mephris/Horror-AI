Just as we moved away from state machine I realized in order to make Behavior tree smart... you need more information and actual planning. 

To be fair, I already had a basic working behavior tree to work with just... it was so fucking stupid. 

So we will be implementing additional systems alongside it, the idea is to create a 4 layer AI structure:

1. The Director - His role is Pacing and Tension control.
	- Basically the idea is to create a movie director, a cheating layer of ai that manipulates the decisionmaking by changing data in the hunters memory. (Additionally he will have access to causing events but its not a part of the HunterAI layer)
	- **Input**: Game Tension, Game Time
	- **Output**: High level goal (searching zones and moving hunter around in order to keep the loosely horror movie tension theory)
2. The Brain - Stragegist, Utility system and HTN (planning jobs)
	- Utility - a layer that keeps the moods of HunterAI, 