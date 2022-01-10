using UnityEngine;

public class NextCellButton : MonoBehaviour
{
    public Vector2 visualCoords;

    public MovementController mc;

    public void OnClick()
    {
        mc.nextMoves.Clear();
        mc.progressSlider.maxValue = mc.moves.Count + 1;
        mc.Move(visualCoords - mc.curPos);
    }
}
