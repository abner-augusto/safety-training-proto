using UnityEngine; // Needed for GameObject

// Represents a player attempting an action
public struct ActionEvent
{
    // The type of action attempted (from our defined list)
    public ActionType type;

    // The GameObject the action was performed on or with (optional, but useful context)
    public GameObject sourceObject;

    // Constructor to easily create an ActionEvent
    public ActionEvent(ActionType type, GameObject sourceObject = null)
    {
        this.type = type;
        this.sourceObject = sourceObject;
    }

    public override string ToString()
    {
        string objName = (sourceObject != null) ? sourceObject.name : "None";
        return $"ActionEvent: Type={type}, SourceObject={objName}";
    }
}