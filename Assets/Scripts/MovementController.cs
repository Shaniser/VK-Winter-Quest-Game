using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using System.Text;
using UnityEngine.SceneManagement;

public class MovementController : MonoBehaviour
{
    public Camera cam;
    public GameObject canvas;
    public GameObject player;
    public GameObject arrowPrefab;
    public GameObject availableCellPrefab;
    public GameObject[] cells;
    public Text dropsCountText;
    public Text scoreText;

    public List<Vector2> moves = new List<Vector2>();
    public List<Vector2Int> improves = new List<Vector2Int>();
    public Stack<Vector2> nextMoves = new Stack<Vector2>();
    
    // For fragment edition
    public Button loadCheckpoint;
    public GameObject checkpointGO;
    private int checkpoint;
    private List<Vector2> movesTail = new List<Vector2>();
    private Vector2 firstVectorTail;
    private Vector2 firstPosTail;
    private GameObject checkpointVector;
    
    // Pathfinding
    public GameObject pathfindingTargetGO;
    public Vector2 pathfindingTarget;
    public Vector2 pathfindingTargetMovement;

    public GameObject arrowHistory;
    
    public Slider progressSlider;

    public Text stepsCounter;

    public float playerSpeed;

    public double score;
    public int drops;
    
    // Global vars
    public Vector2 lastMovementVector = Vector2.zero;
    public Vector2 curPos = Vector2.zero;

    private Dictionary<Vector2Int, GameObject> posToCell = new Dictionary<Vector2Int, GameObject>();

    public static List<List<int>> field = new List<List<int>>();
    private List<GameObject> availableCells = new List<GameObject>();
    private Vector2 startPos;
    private List<int> dropSteps = new List<int>();
    private List<Vector2> foundPath = new List<Vector2>();
    private List<Vector2> foundImproves = new List<Vector2>();

    // Start is called before the first frame update
    void Start()
    {
        field = new List<List<int>>();
        StreamReader sr = new StreamReader("map.json");
        
        var line = sr.ReadLine();
        var lineNumber = 0;
        while (line != null)
        {
            var indexBracket = line.LastIndexOf("[", StringComparison.Ordinal) + 1;
            if (indexBracket != 0)
            {
                var arr = line.Substring(indexBracket, line.IndexOf("]", StringComparison.Ordinal) - indexBracket);
                var numbers = arr.Split(',');
                
                field.Add(new List<int>());
                foreach (var number in numbers)
                {
                    var cellType = int.Parse(number);
                    
                    if (cellType == 3)
                    {
                        curPos = VisualPosition(new Vector2(lineNumber, field[lineNumber].Count));
                        startPos = curPos;
                    }
                    
                    field[lineNumber].Add(cellType);
                }

                lineNumber++;
            }
            
            line = sr.ReadLine();
        }
        
        sr.Close();

        RenderField();

        VisualizeStep();

        SetupCheckpoint();
    }

    void RenderField()
    {
        posToCell.Clear();
        for (var i = 0; i < field.Count; i++)
        {
            for (var j = 0; j < field[i].Count; j++)
            {
                try
                {
                    var visual = VisualPosition(i, j);
                    var cell = Instantiate(cells[field[i][j]], visual, Quaternion.identity);
                    cell.transform.parent = transform;
                    var intVisualPos = new Vector2Int(Mathf.RoundToInt(visual.x), Mathf.RoundToInt(visual.y));
                    posToCell.Add(intVisualPos, cell);
                }
                catch (Exception e)
                {
                    Debug.Log(e);
                    // ignored
                }
            }
        }
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown("d"))
        {
            NextMove();
        }
        if (Input.GetKeyDown("a"))
        {
            Cancel();
        }
        if (Input.GetKeyDown("s"))
        {
            PrintResult();
        }

        if (Input.GetKey(KeyCode.LeftControl))
        {
            if (Input.GetKeyDown(KeyCode.R))
            {
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            }
        }
        
        
        
        Vector2 tempV = cam.ScreenToWorldPoint(Input.mousePosition);
        tempV.x = Mathf.RoundToInt(tempV.x);
        tempV.y = Mathf.RoundToInt(tempV.y);

        if (Input.GetKeyDown(KeyCode.LeftAlt))
        {
            pathfindingTarget = tempV;
            pathfindingTargetGO.transform.position = new Vector3(pathfindingTarget.x, pathfindingTarget.y, -10);
        }
        if (Input.GetKey(KeyCode.LeftAlt))
        {
            foreach (Transform child in pathfindingTargetGO.transform)
            {
                if (!child.CompareTag("Undeletable"))
                {
                    Destroy(child.gameObject);
                }
            }

            if (tempV != pathfindingTarget)
            {
                DrawArrow(pathfindingTarget, tempV - pathfindingTarget, Color.magenta).transform.parent =
                    pathfindingTargetGO.transform;
            }
        }
        if (Input.GetKeyUp(KeyCode.LeftAlt))
        {
            pathfindingTargetMovement = tempV - pathfindingTarget;
            FindPath(Input.GetKey(KeyCode.LeftControl));
        }

        var targetVector3 = new Vector3(curPos.x, curPos.y, player.transform.position.z);
        
        if ((player.transform.position - targetVector3).magnitude > 0.01f)
            player.transform.position = Vector3.Lerp(player.transform.position, targetVector3, playerSpeed * Time.deltaTime);

        stepsCounter.text = "Steps from checkpoint: " + (moves.Count + improves.Count - checkpoint);
        
        var clickCoords = cam.ScreenToWorldPoint(Input.mousePosition);
        if (Input.GetMouseButtonDown(2))
        {
            Improve(clickCoords);
        }
        
        
        

        loadCheckpoint.gameObject.SetActive(Math.Abs(curPos.x - firstPosTail.x) < 0.001f
                                            && Math.Abs(curPos.y - firstPosTail.y) < 0.001f
                                            && Math.Abs(lastMovementVector.x - firstVectorTail.x) < 0.001f
                                            && Math.Abs(lastMovementVector.y - firstVectorTail.y) < 0.001f);
    }

    static Vector2Int FieldPosition(Vector2 coords)
    {
        return new Vector2Int(-Mathf.RoundToInt(coords.y), Mathf.RoundToInt(coords.x));
    }
    
    Vector3 VisualPosition(float x, float y, float z = 0)
    {
        return new Vector3(y, -x, z);
    }

    Vector3 VisualPosition(Vector2 coords, float z = 0)
    {
        return VisualPosition(coords.x, coords.y, z);
    }

    public void Move(Vector2 movement, bool isPrecalculation = false)
    {
        var nextCell = curPos + movement;
        if (!GetAvailableSteps(curPos, lastMovementVector).Contains(nextCell))
        {
            throw new Exception("This step is not available");
        }
        
        moves.Add(movement);

        if (!isPrecalculation)
        {
            foreach (Transform child in arrowHistory.transform) {
                if (child.gameObject != checkpointVector) Destroy(child.gameObject);
            }

            var curLocalPos = startPos;
            for (var i = 0; i < moves.Count; i++)
            {
                var delta = moves[i];
                DrawArrow(curLocalPos, delta, 0.5f);
                curLocalPos += delta;
            }

            DrawArrow(curPos, movement);
            DrawArrow(nextCell, movement, new Color(0, 0, 0, 0.5f));
        }

        curPos = nextCell;
        lastMovementVector = movement;

        if (!isPrecalculation)
        {
            var curFieldPos = FieldPosition(curPos);
            if (field[curFieldPos.x][curFieldPos.y] == 4)
            {
                field[curFieldPos.x][curFieldPos.y] = 2;
                dropSteps.Add(moves.Count - 1);
                drops++;
                posToCell[new Vector2Int((int)curPos.x, (int)curPos.y)].GetComponent<SpriteRenderer>()
                    .enabled = false;
                dropsCountText.text = drops.ToString();
            }
            
            score = CountScore();
            scoreText.text = score.ToString();
            
            progressSlider.value = moves.Count;
            
            VisualizeStep();
        }
    }

    public void Cancel(bool isPrecalculation = false)
    {
        var lastMove = moves[moves.Count - 1];
        moves.RemoveAt(moves.Count - 1);

        if (!isPrecalculation)
        {
            foreach (Transform child in arrowHistory.transform) {
                if (child.gameObject != checkpointVector) Destroy(child.gameObject);
            }

            var curLocalPos = startPos;
            for (var i = 0; i < moves.Count; i++)
            {
                var delta = moves[i];
                DrawArrow(curLocalPos, delta, 0.5f);
                curLocalPos += delta;
            }
        }

        curPos -= lastMove;

        var prevMove = moves.Count == 0 ? Vector2.zero : moves[moves.Count - 1];

        lastMovementVector = prevMove;

        if (isPrecalculation) return;
        
        DrawArrow(curPos - prevMove, prevMove);
        DrawArrow(curPos, prevMove);

        if (dropSteps.Contains(moves.Count))
        {
            var dropPos = new Vector2Int((int)(curPos.x + lastMove.x), (int)(curPos.y + lastMove.y));
            var dropFieldPos = FieldPosition(dropPos);
            field[dropFieldPos.x][dropFieldPos.y] = 4;
            dropSteps.Remove(moves.Count);
            drops--;
            posToCell[dropPos].GetComponent<SpriteRenderer>()
                .enabled = true;
            dropsCountText.text = drops.ToString();
        }

        score = CountScore();
        scoreText.text = score.ToString();
            
        VisualizeStep();
        
        progressSlider.value = moves.Count;
        
        nextMoves.Push(lastMove);
    }

    public GameObject DrawArrow(Vector2 start, Vector2 delta, float alpha = 1f, bool isGreen = false)
    {
        return DrawArrow(start, delta, new Color(0, isGreen ? 1 : 0, 0, alpha));
    }

    public GameObject DrawArrow(Vector2 start, Vector2 delta, Color color)
    {
        var arrow = Instantiate(arrowPrefab, arrowHistory.transform);
        try
        {
            arrow.transform.localScale = new Vector3(delta.magnitude, 1, 1);
            arrow.transform.position = new Vector3(start.x, start.y, -0.5f);
            var degrees = Mathf.Atan(Mathf.Abs(delta.y / delta.x)) * 180 / Mathf.PI;
            if (delta.x < 0)
            {
                if (delta.y < 0)
                {
                    degrees += 180;
                }
                else
                {
                    degrees = 180 - degrees;
                }
            }
            else
            {
                if (delta.y < 0)
                {
                    degrees = -degrees;
                }
            }

        
            arrow.transform.rotation = Quaternion.Euler(0, 0, degrees);

            arrow.GetComponentInChildren<SpriteRenderer>().color = color;
        }
        catch (Exception e)
        {
            // Debug.Log(e);
        }

        return arrow;
    }

    List<Vector2> GetAvailableSteps(Vector2 curPosLocal, Vector2 lastMovementVectorLocal, int improvesCount = 0)
    {
        var availableSteps = new List<Vector2>();

        var fieldCurPos = FieldPosition(curPosLocal);

        if (fieldCurPos.x < 0 || fieldCurPos.y < 0 || fieldCurPos.x >= field.Count || fieldCurPos.y >= field[0].Count)
            return availableSteps;

        var fieldCell = field[fieldCurPos.x][fieldCurPos.y];
        
        var radius = (fieldCell + (fieldCell + improvesCount < 3 ? improvesCount : 0)) switch
        {
            0 => 1,
            1 => 2,
            9 => 0,
            _ => 3
        };

        var nextVectorPos = curPosLocal + lastMovementVectorLocal;

        for (var x = nextVectorPos.x - radius + 1; x < nextVectorPos.x + radius; x++)
        {
            for (var y = nextVectorPos.y - radius + 1; y < nextVectorPos.y + radius; y++)
            {
                var fieldCellNextPos = FieldPosition(new Vector2(x, y));
                
                if (fieldCellNextPos.x >= field.Count
                    || fieldCellNextPos.y >= field[0].Count
                    || Math.Abs(curPosLocal.x - x) < 0.001f
                        && Math.Abs(curPosLocal.y - y) < 0.001f
                        && lastMovementVectorLocal == Vector2.zero)
                    continue;
                
                availableSteps.Add(new Vector2(x, y));
            }
        }

        return availableSteps;
    }

    void VisualizeStep()
    {
        var maxSteps = 20000;
        var steps = 0;
        var maxDepth = 0;
        
        var availablePositions = GetAvailableSteps(curPos, lastMovementVector);

        var safetySteps = new List<Vector2>(availablePositions);

        bool isAllUnavailable(Vector2 pos, int depth)
        {
            if (steps++ > maxSteps) return false;
            
            var result = false;
            
            Move(pos - curPos, true);
            
            var localAvailablePositions = GetAvailableSteps(curPos, lastMovementVector);

            if (localAvailablePositions.Count == 0) result = true;

            if (depth <= maxDepth)
            {
                localAvailablePositions.RemoveAll(pos1 => isAllUnavailable(pos1, depth + 1));
            }
            
            if (localAvailablePositions.Count == 0) result = true;

            Cancel(true);

            return result;
        }

        while (steps < maxSteps && safetySteps.Count > 0)
        {
            safetySteps.RemoveAll(pos1 => isAllUnavailable(pos1, 0));
            maxDepth++;
        }

        while (availableCells.Count > 0)
        {
            var cellBtn = availableCells[0];
            Destroy(cellBtn);
            availableCells.RemoveAt(0);
        }
        
        foreach (var available in availablePositions)
        {
            var availableCell = Instantiate(availableCellPrefab, new Vector3(available.x, available.y, -10), Quaternion.identity);
            var ncb = availableCell.GetComponent<NextCellButton>();
            ncb.mc = this;
            ncb.visualCoords = new Vector2(available.x, available.y);
            availableCell.GetComponent<NextCellButton>().mc = this;
            availableCell.transform.parent = canvas.transform;
            if (!safetySteps.Contains(available)) availableCell.GetComponent<Image>().color = Color.red;
            availableCells.Add(availableCell);
        }
    }

    private double CountScore()
    {
        return 3600.0 * Math.Pow(1.1, drops) / (moves.Count + improves.Count);
    }

    public void PrintResult()
    {
        using(StreamWriter writetext = new StreamWriter(score + ".txt"))
        {
            var sb = new StringBuilder();
            foreach (var improve in improves)
            {
                sb.Append(VectorToString(improve));
            }

            sb.Append('\n');
            
            foreach (var move in moves)
            {
                sb.Append(VectorToString(move));
            }
            
            writetext.WriteLine(sb.ToString());
            
            Debug.Log(sb.ToString());
        }
    }

    public void SliderChanged()
    {
        var cur = progressSlider.value;

        // Cancelling
        while (moves.Count > cur)
        {
            if (Math.Abs(moves.Count - 1 - cur) < 0.0001f)
            {
                Cancel();
            }
            else
            {
                var lastMove = moves[moves.Count - 1];
                moves.RemoveAt(moves.Count - 1);

                curPos -= lastMove;

                var prevMove = moves.Count == 0 ? Vector2.zero : moves[moves.Count - 1];

                lastMovementVector = prevMove;

                nextMoves.Push(lastMove);
                
                if (dropSteps.Contains(moves.Count))
                {
                    var dropPos = new Vector2Int((int)(curPos.x + lastMove.x), (int)(curPos.y + lastMove.y));
                    var dropFieldPos = FieldPosition(dropPos);
                    field[dropFieldPos.x][dropFieldPos.y] = 4;
                    dropSteps.Remove(moves.Count);
                    drops--;
                    posToCell[dropPos].GetComponent<SpriteRenderer>()
                        .enabled = true;
                    dropsCountText.text = drops.ToString();
                }
            }
        }
        
        // Next stepping
        while (moves.Count < cur)
        {
            var curFieldPos = FieldPosition(curPos);
            if (field[curFieldPos.x][curFieldPos.y] == 4)
            {
                field[curFieldPos.x][curFieldPos.y] = 2;
                dropSteps.Add(moves.Count - 1);
                drops++;
                posToCell[new Vector2Int((int)curPos.x, (int)curPos.y)].GetComponent<SpriteRenderer>()
                    .enabled = false;
                dropsCountText.text = drops.ToString();
            }
            if (nextMoves.Count != 0) Move(nextMoves.Pop(), Math.Abs(moves.Count + 1 - cur) > 0.0001f);
        }
        
        score = CountScore();
        scoreText.text = score.ToString();
    }

    public void LoadSolution()
    {
        StreamReader sr = new StreamReader("input.txt");
        
        var line = sr.ReadLine();
        var lineNumber = 0;
        while (line != null)
        {
            var vectorStrings = line.Split(new string[] { "], [" }, StringSplitOptions.None);
            
            if (vectorStrings.Length != 0)
            {
                switch (lineNumber)
                {
                    case 0:
                    {
                        improves.Clear();
                        
                        foreach (var vectorStr in vectorStrings.Reverse())
                        {
                            try
                            {
                                var parsedVector = ParseVector2(vectorStr);
                                Improve(parsedVector);
                            }
                            catch (Exception e)
                            {
                                Debug.Log(e);
                            }
                        }

                        break;
                    }
                    case 1:
                    {
                        progressSlider.value = 0;
                    
                        moves.Clear();
                        nextMoves.Clear();
                
                        foreach (var vectorStr in vectorStrings.Reverse())
                        {
                            try
                            {
                                nextMoves.Push(ParseVector2(vectorStr));
                            }
                            catch (Exception e)
                            {
                                Debug.Log(e);
                            }
                        }

                        progressSlider.maxValue = moves.Count + nextMoves.Count;
                        progressSlider.value = progressSlider.maxValue;
                        break;
                    }
                }
            }

            lineNumber++;
            
            line = sr.ReadLine();
        }
        
        sr.Close();
        
        Cancel();
        NextMove();
    }

    public void NextMove()
    {
        if (nextMoves.Count != 0) Move(nextMoves.Pop());
    }

    private string VectorToString(Vector2 v)
    {
        return "[" + v.x + ", " + -v.y + "], ";
    }
    
    private Vector2 ParseVector2(String str)
    {
        var coords = str.Substring(str.LastIndexOf("[", StringComparison.Ordinal) + 1).Split(',');

        try
        {
            new Vector2(float.Parse(coords[0]), -float.Parse(coords[1].Trim(new char[] {']', ',', ' '})));
        }
        catch (Exception e)
        {
            Debug.Log(e);
        }
        
        return new Vector2(float.Parse(coords[0]), -float.Parse(coords[1].Trim(new char[] {']', ',', ' '})));
    }

    public void SetupCheckpoint()
    {
        checkpoint = moves.Count + improves.Count;
        checkpointGO.transform.position = curPos;
        movesTail.Clear();
        movesTail.AddRange(nextMoves);
        firstPosTail = new Vector2(curPos.x, curPos.y);
        firstVectorTail = new Vector2(lastMovementVector.x, lastMovementVector.y);

        try
        {
            Destroy(checkpointVector);
        }
        catch (Exception e)
        {
            // ignored
        }

        checkpointVector = DrawArrow(curPos, lastMovementVector, 0.5f, true);
    }

    public void ConnectFragment()
    {
        if (Math.Abs(curPos.x - firstPosTail.x) > 0.001f
            || Math.Abs(curPos.y - firstPosTail.y) > 0.001f
            || Math.Abs(lastMovementVector.x - firstVectorTail.x) > 0.001f
            || Math.Abs(lastMovementVector.y - firstVectorTail.y) > 0.001f) return;
        
        nextMoves.Clear();

        var nextMovesTail = new List<Vector2>();
        nextMovesTail.AddRange(movesTail);
        nextMovesTail.Reverse();
        
        foreach (var move in nextMovesTail)
        {
            nextMoves.Push(move);
        }

        progressSlider.value = 0;
        progressSlider.maxValue = nextMoves.Count;
        progressSlider.value = progressSlider.maxValue;
    }

    public void Improve(Vector2 cell)
    {
        var fieldVector = FieldPosition(cell);
        var curField = field[fieldVector.x][fieldVector.y];
        var vectorInt = new Vector2Int(Mathf.RoundToInt(cell.x), Mathf.RoundToInt(cell.y));

        var isUsed = false;
        var curLocalPos = startPos;
        foreach (var move in moves)
        {
            curLocalPos += move;
            var intPos = new Vector2Int(Mathf.RoundToInt(curLocalPos.x), Mathf.RoundToInt(curLocalPos.y));
            if (intPos != vectorInt) continue;
            isUsed = true;
            break;
        }
        
        if (isUsed) return;

        var delta = 0;
        
        switch (curField)
        {
            case 0:
            case 1:
                improves.Add(vectorInt);
                delta++;
                break;
            default:
                delta -= improves.RemoveAll(v => v == vectorInt);
                break;
        }

        field[fieldVector.x][fieldVector.y] += delta;

        if (delta == 0) return;
        
        Destroy(posToCell[vectorInt]);

        posToCell[vectorInt] = Instantiate(cells[field[fieldVector.x][fieldVector.y]], new Vector3(Mathf.Round(cell.x), Mathf.Round(cell.y), 0), Quaternion.identity);
        posToCell[vectorInt].transform.parent = transform;
        
        for (var k = 0; k < improves.Count(v => v == vectorInt); k++)
        {
            posToCell[vectorInt].transform.GetChild(k).GetComponent<SpriteRenderer>().enabled = true;
        }

        var curMove = moves.Count;
        progressSlider.value = 0;
        progressSlider.value = curMove;
    }







    public void PrintSolution()
    {
        var sb = new StringBuilder();
        foreach (var improve in improves)
        {
            sb.Append(VectorToString(improve));
        }
           
        var sb1 = new StringBuilder();
        foreach (var move in moves)
        {
            sb1.Append(VectorToString(move));
        }

        if (sb.Length > 1) sb.Remove(sb.Length - 2, 2);
        if (sb1.Length > 1) sb1.Remove(sb1.Length - 2, 2);
        
        var str = @"{ 
  ""job"": [" 
            + sb +
  @"],
  ""path"": ["
            + sb1 +
   @"]
}";

        using var writeText = new StreamWriter("Solution" + score + ".json");
        writeText.WriteLine(str);
    }

    struct State : IComparable
    {
        public Vector2 pos, lastMovement, targetPos, targetMovement;
        public List<State> history;

        public bool isImprove;

        public State(Vector2 pos, Vector2 lastMovement, Vector2 targetPos, Vector2 targetMovement, List<State> history)
        {
            this.pos = pos;
            this.lastMovement = lastMovement;
            this.targetPos = targetPos;
            this.targetMovement = targetMovement;
            this.history = history;
            isImprove = false;
        }
        
        public State(bool isImprove, Vector2 pos)
        {
            this.pos = pos;
            lastMovement = default;
            targetPos = default;
            targetMovement = default;
            history = null;
            this.isImprove = isImprove;
        }

        public float Measure()
        {
            if (targetPos == pos && targetMovement == lastMovement) return -100000;
            var fieldVector = FieldPosition(pos);
            var isDrop = false;
            if (fieldVector.x < field.Count - 1
                && fieldVector.y < field.Count - 1
                && fieldVector.x > 0
                && fieldVector.y > 0)
            {
                isDrop = field[fieldVector.x][fieldVector.y] == 4;
            }

            return (targetPos - targetMovement - pos).magnitude + (history.Count
                                                                   // - (history.Count > 0 && history.Last().isImprove ? 1 : 0)
                                                                   - (isDrop ? 0.25f : 0)) * 7 +
                   (targetPos - pos - targetMovement).magnitude * (history.Count) / 3f;
            // (targetPos - targetMovement - (pos + lastMovement)).magnitude -
            // (targetPos - targetMovement - pos).magnitude

            // - lastMovement.magnitude +
            // (history.Count - (isDrop ? 1 : 0)) * 7 +
            // (targetPos - pos - targetMovement).magnitude * (history.Count) / 3f;
            //        + Vector2.Angle(targetMovement, lastMovement) * Vector2.Angle(targetMovement, targetPos - pos) / (targetPos - pos).magnitude / 180
            //        + Vector2.Dot(targetMovement, targetPos - pos) / targetMovement.magnitude > 1f &&
            //        Vector2.Angle(targetMovement, lastMovement) < 90
            // ? 20
            // : 0
            // + history.Count * (targetPos - pos).magnitude;
        }

        public int CompareTo(object obj)
        {
            var other = (State)obj;

            return (int)Mathf.Sign(Measure() - other.Measure());
        }

        public override string ToString()
        {
            return "[" + pos.x + ", " + pos.y + "] Score: " + Measure() + " " + (history.Count > 0 && history.Last().isImprove ? "Improved" : "");
        }
    }

    public Transform pathfindingTransform;
    public GameObject pathGO;
    public GameObject pathImproveGO;
    public GameObject pathProcessingGO;
    public void FindPath(bool useImprovements = true)
    {
        var path = AStar(pathfindingTarget, pathfindingTargetMovement, useImprovements);

        if (path == null)
        {
            pathfindingTargetGO.GetComponentInChildren<Text>().text = "0";
            Debug.Log("Can not find path");
            return;
        }
        
        var str = "";
        
        foreach (Transform child in pathfindingTransform)
        {
            if (!child.CompareTag("AStar"))
            {
                Destroy(child.gameObject);
            }
        }
        
        foreach (var state in path)
        {
            Instantiate(state.isImprove ? pathImproveGO : pathGO,
                new Vector3(state.pos.x, state.pos.y, (state.isImprove ? pathImproveGO : pathGO).transform.position.z),
                Quaternion.identity).transform.parent = pathfindingTransform;
            str += "[" + state.pos.x + ", " + state.pos.y + "] -> ";
        }

        pathfindingTargetGO.GetComponentInChildren<Text>().text = path.Count.ToString();

        foundPath.Clear();
        foundImproves.Clear();
        foreach (var state in path)
        {
            if (state.isImprove)
            {
                foundImproves.Add(new Vector2(state.pos.x, state.pos.y));
            }
            else
            {
                foundPath.Add(new Vector2(state.pos.x, state.pos.y));
            }
        }

        str += "[" + firstPosTail.x + ", " + firstPosTail.y + "]";
        Debug.Log(str);
    }

    List<State> AStar(Vector2 targetPos, Vector2 targetMovement, bool useImprovements = true)
    {
        List<State> resultPath = null;
        var visited = new List<State>();
        var priorityQueue = new MinHeap<State>{new State(curPos, lastMovementVector, targetPos, targetMovement, new List<State>())};
        while (priorityQueue.Count > 0 && visited.Count < 6000)
        {
            var x = priorityQueue.ExtractDominating();
            if (visited.Any(state => state.pos == x.pos && state.lastMovement == x.lastMovement)) continue;
            if (x.pos == targetPos && x.lastMovement == targetMovement)
            {
                resultPath = x.history;
                break;
            }
            visited.Add(x);
            var nextPositions = GetAvailableSteps(x.pos, x.lastMovement);

            var history = new List<State>(x.history) { x };
            var nextStates = nextPositions.Select(nextPosition =>
                    new State(nextPosition, nextPosition - x.pos, targetPos, targetMovement, history)
                ).ToList();
            foreach (var nextState in nextStates)
            {
                priorityQueue.Add(nextState);
            }

            if (!useImprovements) continue;
            var improvedPositions = GetAvailableSteps(x.pos, x.lastMovement, 1);
            foreach (var additionalPos in improvedPositions.Except(nextPositions))
            {
                // if (improvedPos == targetPos && improvedPos - x.pos == targetMovement) continue;
                
                var historyImproved = new List<State>(x.history) { x, new State(true, x.pos) };
                var nextStateWithCurrentImprove = new State(additionalPos, additionalPos - x.pos, targetPos,
                    targetMovement, historyImproved);
                priorityQueue.Add(nextStateWithCurrentImprove);
            }
        }
        
        foreach (Transform child in pathfindingTransform)
        {
            if (!child.CompareTag("Undeletable"))
            {
                Destroy(child.gameObject);
            }
        }

        for (var i = 0; i < visited.Count; i++)
        {
            var state = visited[i];
            var processingMark = Instantiate(pathProcessingGO,
                new Vector3(state.pos.x, state.pos.y, pathProcessingGO.transform.position.z),
                Quaternion.identity);
            processingMark.tag = "AStar";
            processingMark.transform.parent = pathfindingTransform;
            processingMark.name = "State: " + state.Measure() + (state.history.Count > 0 && state.history[state.history.Count - 1].isImprove ? " Improved" : "");

            var color = Color.Lerp(Color.white, Color.magenta, 
                // i / (float)visited.Count
                1f
            );
            color.a = Mathf.Clamp01(1f / (visited.Count / 50f)) * 0.1f;
            processingMark.GetComponent<SpriteRenderer>().color = color;
        }

        return resultPath;
    }

    public void ApplyPath()
    {
        nextMoves.Clear();
        bool isOk = false;
        var curVector = curPos;
        var movesStack = new List<Vector2>();
        
        for (var i = 0; i < foundPath.Count; i++)
        {
            if (!isOk)
            {
                if (foundPath[i] == curPos)
                {
                    isOk = true;
                }
                continue;
            }
            
            // nextMoves.Push(foundPath[i] - curVector);
            
            movesStack.Add(foundPath[i] - curVector);

            curVector = foundPath[i];
        }

        if (isOk)
        {
            movesStack.Add(pathfindingTarget - curVector);
            movesStack.Reverse();
            foreach (var move in movesStack)
            {
                nextMoves.Push(move);
            }
            foreach (var foundImprove in foundImproves)
            {
                Improve(foundImprove);
            }
        }

        progressSlider.maxValue = moves.Count + nextMoves.Count;
        progressSlider.value = 0;
        progressSlider.value = progressSlider.maxValue;
    }
}


