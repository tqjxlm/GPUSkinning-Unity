using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

public class SceneGrid: MonoBehaviour
{
    int CellSize = 2;
    int GridSize = 256;

    [HideInInspector]
    public float sqrRadius;
    public Vector2 center;

    const int MAX_QUERY = 9;

    HashSet<int>[] cells;
    int colCnt;
    int cellCnt;
    int rootX;
    int rootY;

    int cellPow;
    int colPow;

    int[] directions;
    int[] queryResults = new int[MAX_QUERY];


    void Start()
    {
        // Grid info
        colCnt = (GridSize - 1) / CellSize + 1;
        cellCnt = colCnt * colCnt;
        rootX = (int)center.x - colCnt / 2 * CellSize;
        rootY = (int)center.y - colCnt / 2 * CellSize;
        cells = new HashSet<int>[cellCnt];
        for (int i = 0; i < cellCnt; i++)
        {
            cells[i] = new HashSet<int>();
        }
        for (int i = 0; i < MAX_QUERY; i++)
        {
            queryResults[i] = -1;
        }

        // Multiplication optimization
        cellPow = (int)(Math.Log(CellSize, 2) + 0.5);
        colPow = (int)(Math.Log(colCnt, 2) + 0.5);

        // For agent query
        directions = new int[8] { 1, colCnt + 1, colCnt, colCnt - 1, -1, -colCnt - 1, -colCnt, -colCnt + 1 };
    }

    // Return the final cell id if not collided
    // else return -1
    public int Move(int id, int from, Vector3 dst, Vector3[] positions)
    {
        int to = GetCellNumber(dst.x, dst.z);
        if (to < 0)
        {
            return -1;
        }

        // Collision detection
        //foreach (int neighbour in GetNeighbours(to, dst - positions[id]))
        //{
        //    if (neighbour == -1)
        //    {
        //        break;
        //    }
            foreach (int agent in cells[to])
            {
                if (agent != id)
                {
                    if ((positions[agent] - dst).sqrMagnitude < sqrRadius)
                    {
                        return -1;
                    }
                }
            }
        //}

        if (to == from)
        {
            return from;
        }

        // Move the agent between cells
        if (from >= 0)
        {
            cells[from].Remove(id);
        }
        cells[to].Add(id);

        return to;
    }

    int EncodeCellNumber(int x, int y)
    {
        if (x < 0 || y < 0)
        {
            return -1;
        }
        int encoded = (x << colPow) + y;
        return encoded < cellCnt ? encoded : -1;
    }

    public int GetCellNumber(float x, float y)
    {
        return EncodeCellNumber(((int)x - rootX) >> cellPow, ((int)y - rootY) >> cellPow);
    }

    int GetDirection(Vector3 speed)
    {
        float tan = speed.z / (speed.x + float.Epsilon);
        if (tan > Math.Tan(-Math.PI / 6) && tan < Math.Tan(Math.PI / 6))
        {
            return speed.x > 0 ? 0 : 4;
        }
        if (tan > Math.Tan(Math.PI / 6) && tan < Math.Tan(Math.PI / 3))
        {
            return speed.z > 0 ? 1 : 5;
        }
        if (tan > Math.Tan(Math.PI / 3) || tan < Math.Tan(-Math.PI / 3))
        {
            return speed.z > 0 ? 2 : 6;
        }
        return speed.z > 0 ? 3 : 7;
    }

    void CollectAgents(ref int resCnt, int cell)
    {
        if (cell < 0 || cell >= cellCnt)
        {
            return;
        }

        foreach (int agent in cells[cell])
        {
            if (resCnt == MAX_QUERY)
            {
                return;
            }

            queryResults[resCnt] = agent;
            resCnt += 1;
        }
    }

    public ref readonly int[] GetNeighbours(int cell, Vector3 speed)
    {
        // Fill the query for every neighbour
        int resCnt = 0;
        int dir = GetDirection(speed);
        CollectAgents(ref resCnt, cell);
        CollectAgents(ref resCnt, cell + directions[dir]);
        CollectAgents(ref resCnt, cell + directions[(dir-1+directions.Length) % directions.Length]);
        CollectAgents(ref resCnt, cell + directions[(dir+1) % directions.Length]);

        // Invalidate the rest
        if (resCnt < queryResults.Length)
        {
            queryResults[resCnt] = -1;
        }

        return ref queryResults;
    }
}