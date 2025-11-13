using UnityEngine;

public class Room : MonoBehaviour
{
    public PatrolPoints[] patrolPoint;

    // We can keep exitCount public but now it's calculated, not set.
    [HideInInspector] // Hide this in the Inspector since it's calculated
    public int exitCount;

    [SerializeField] public bool isNearbyPlayer; // You can keep this serialized

    private void Awake()
    {
        patrolPoint = GetComponentsInChildren<PatrolPoints>();

        // --- NEW LOGIC FOR DYNAMIC EXIT COUNT ---
        CountExits();
    }

    private void CountExits()
    {
        int count = 0;

        // We iterate through all direct children of the Room
        foreach (Transform child in transform)
        {
            // Check if the child has the "RoomExit" tag
            if (child.CompareTag("RoomExit"))
            {
                count++;
            }
        }

        this.exitCount = count;

        // You can add a Debug.Log here to verify the count on start!
        // Debug.Log($"{gameObject.name} found {exitCount} exits.");
    }


    private void OnDrawGizmos()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawCube(transform.position, Vector3.one * 0.3f);
    }
}