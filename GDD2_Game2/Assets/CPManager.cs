﻿using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

public class CPManager : MonoBehaviour {
    MapInfo mi;

    //list of all active control points
    HashSet<ControlPoint> CPs = new HashSet<ControlPoint>();

    //field of movement vectors
    Vector2[,] vectorField;
    float[,] cpDistancesSqr;
    Vector2 vfSize;
    //Vector2 mapPos;

    bool drawingVF = false;

    bool recalculateVF = false;
    [SerializeField]
    int recalcInterval = 5;
    [SerializeField]
    int vfPrecision = 1;
    [SerializeField]
    float cpRadius = 100.0f;
    [SerializeField]
    float cpDeadZone = 10.0f;

    void Awake()
    {
        
    }

	// Use this for initialization
	void Start () {
        mi = (MapInfo)gameObject.GetComponent(typeof(MapInfo));
        //get the map's collider
        //BoxCollider2D mapBox = (BoxCollider2D)GameObject.Find("Map").GetComponent(typeof(BoxCollider2D));
        vfSize = mi.MapSize / vfPrecision; //vf size is the collider's size divided by the precision
        //mapPos is the position of the vf's bottom-left corner in world space
        //the vf is bottom-left anchored, but the collider is center anchored. we have to get the collider's bottom-left corner
        //mapPos = new Vector2(mapBox.transform.position.x - mapBox.size.x / 2, mapBox.transform.position.y - mapBox.size.y / 2);

        //change to use precision global, scaled to the map's aspect ratio
        vectorField = new Vector2[(int)vfSize.x, (int)vfSize.y];
        cpDistancesSqr = new float[(int)vfSize.x, (int)vfSize.y];
        //fill with 0,0s to start
        for (int n = 0; n < vectorField.GetLength(0); n++)
            for (int c = 0; c < vectorField.GetLength(1); c++)
                vectorField[n, c] = new Vector2(0, 0);
        for (int n = 0; n < cpDistancesSqr.GetLength(0); n++)
            for (int c = 0; c < cpDistancesSqr.GetLength(1); c++)
                cpDistancesSqr[n, c] = float.MaxValue;
    }
	
	// Update is called once per frame
	void Update () {
        //debug lines
        if (Application.isEditor && !drawingVF)
            StartCoroutine(DrawVectorField());
            

        //check for queued VF recalculations at every x frames (where x is recalcInterval)
        if (recalculateVF && Time.frameCount % recalcInterval == 0)
        {
            recalculateVF = false;
            StartCoroutine("RecalculateVectorField");
        }

        if (Input.GetKeyDown(KeyCode.Q))
        {
            ClearCPs();
        }
        //if(Input.GetMouseButtonDown(0))
    }

    IEnumerator DrawVectorField()
    {
        drawingVF = true;
        int pointsThisFrame = 0;
        for (int n = 0; n < vectorField.GetLength(0); n++)
            for (int c = 0; c < vectorField.GetLength(1); c++)
            {
                Debug.DrawRay(mi.GridToWorldSpace((new Vector3(n, c, -3)), vfPrecision), (Vector3)vectorField[n, c].normalized, Color.magenta, Time.deltaTime * 30);
                pointsThisFrame++;
                if (pointsThisFrame > 1000)
                {
                    yield return null;
                    pointsThisFrame = 0;
                }
            }
        drawingVF = false;
    }

    void ClearCPs()
    {
        foreach(ControlPoint cp in CPs)
        {
            Destroy(cp);
        }
        CPs.Clear();
    }

    public void AddCP(ControlPoint cp)
    {
        CPs.Add(cp);
        QueueVFRecalculation();
    }

    public void RemoveCP(ControlPoint cp)
    {
        CPs.Remove(cp);
        //skip a frame to allow the CP to kill itself before we start
        Destroy(cp.gameObject);
        StopCoroutine("RecalculateVectorField");
        QueueVFRecalculation();
    }

    IEnumerator RecalculateVectorField()
    {
        //only process as many frames as necessary to get the vf recalculated within the recalc window
        int calculationsPerFrame = (int)Math.Ceiling((vfSize.x * vfSize.y) / recalcInterval);

        //holds the vectors from current point to each control point
        List<Vector2> cpVectors = new List<Vector2>(CPs.Count);
        ControlPoint[] cps = new ControlPoint[CPs.Count];
        CPs.CopyTo(cps);

        //iterate through the dimensions of the vector field
        for (int row = 0; row < vectorField.GetLength(0); row++)
        {
            for (int col = 0; col < vectorField.GetLength(1); col++)
            {
                //reinitialize cp vectors
                cpVectors.Clear();

                //get the current point in VF space
                Vector2 p = new Vector2(row, col);

                //tracks the magnitude of the longest cp vector
                float longestMagnitudeSqr = 0.0f;
                float shortestMagnitudeSqr = float.MaxValue;
                Vector2 firstCPVector = new Vector2(0,0);
                //iterate through length of cp list
                int c = 0;
                int numInRange = 0;
                bool inDeadZone = false;
                foreach(ControlPoint cp in cps)
                {
                    //ControlPoint cp = [c];
                    Vector2 cpPosition = mi.WorldToGridSpace(new Vector2(cp.transform.position.x, cp.transform.position.y), vfPrecision);
                    //cp vector is vector from position to cp position
                    Vector2 cpPositionRelativeToGridPoint = cpPosition - p;
                    c++;
                    if ((cpPositionRelativeToGridPoint.sqrMagnitude < (cpRadius / vfPrecision) * (cpRadius / vfPrecision)))
                    {
                        //if the cp is farther than the deadzone radius, we're good
                        if (!(cpPositionRelativeToGridPoint.sqrMagnitude < (cpDeadZone / vfPrecision) * (cpDeadZone / vfPrecision)))
                        {
                            cpVectors.Add(cpPositionRelativeToGridPoint*cp.Strength);
                            numInRange++;
                        }
                        //otherwise, mark that this point is dead and break
                        else
                        {
                            inDeadZone = true;
                            break;
                        }
                    }
                    else
                        continue;

                    //check for new longest magnitude
                    if (cpPositionRelativeToGridPoint.sqrMagnitude > longestMagnitudeSqr) longestMagnitudeSqr = cpPositionRelativeToGridPoint.sqrMagnitude;
                    if (cpPositionRelativeToGridPoint.sqrMagnitude < shortestMagnitudeSqr) shortestMagnitudeSqr = cpPositionRelativeToGridPoint.sqrMagnitude;
                }

                //we'll multiply all vectors by a scale factor so that the closest ones matter more
                //furthest vector is 0, <0,0> is 1
                //float scaleFactorBase = 1 - (1 / longestMagnitude);
                Vector2 calculatedVector = new Vector2(0, 0);
                //leave the vector at 0,0 if the point is in a dead zone
                if (!inDeadZone)
                {
                    //if there's only one CP, don't apply scale factor
                    if (cpVectors.Count == 1 || numInRange == 1)
                        calculatedVector = cpVectors[0];
                    //otherwise...
                    else
                        //add all scaled cp vectors
                        for (int n = 0; n < cpVectors.Count; n++)
                        {
                            Vector2 v = cpVectors[n];
                            if (v != Vector2.zero)
                                calculatedVector += v * (longestMagnitudeSqr - v.sqrMagnitude) / v.sqrMagnitude;
                        }
                }
                else
                    shortestMagnitudeSqr = float.MaxValue;

                //final vector is the sum normalized
                vectorField[row, col] = calculatedVector;
                cpDistancesSqr[row, col] = shortestMagnitudeSqr;

                //if we've done the last in this batch, stop until the next frame
                if ((row * col) % calculationsPerFrame == calculationsPerFrame - 1) yield return null;
            }
        }
    }

    //Gets estimated field vector for a given position
    //Averages nearest four pre-computed field vectors with weightings proportional to their closeness
    public Vector2 GetVectorAtPosition(Vector2 pos)
    {
        //correct for map offset
        pos = mi.WorldToGridSpace(pos,vfPrecision);
        //if we're off the map, return normalized vector toward map center
        if (!mi.IsGridPosOnMap(pos, vfPrecision))
            return (mi.GridToWorldSpace(new Vector2(vfSize.x / 2, vfSize.y / 2) - pos, vfPrecision)).normalized;

        Vector2 returnVector = new Vector2(0, 0);

        //get the nearest ints for both dimensions in both directions
        int xUp = (int)Math.Ceiling(pos.x);
        int xDown = (int)Math.Floor(pos.x);
        int yUp = (int)Math.Ceiling(pos.y);
        int yDown = (int)Math.Floor(pos.y);

        //loop in 2d from the bottom ints to the top
        for (int n = xDown; n <= xUp; n++)
        {
            for (int c = yDown; c <= yUp; c++)
            {
                //get the field vector for the current ints
                Vector2 nearVec = vectorField[n, c];
                //scale factor, closer is stronger
                float scaleFactor = (1 - Math.Abs(n - pos.x)) * (1 - Math.Abs(c - pos.y));
                //add scaled field vector to return vector
                returnVector += nearVec * scaleFactor;
                //Debug.DrawRay(new Vector2(n, c), nearVec * scaleFactor, Color.blue, Time.deltaTime);
            }
        }

        //return normalized return vector
        return returnVector.normalized;
    }

    //gets estimated distance from control point by averaging nearest four values in cpDistances array
    //same as the above method, but returns a float, and pulls from a diferent array
    public float GetDistanceSqrFromCPAtPos(Vector2 pos)
    {
        //correct for map offset
        pos = mi.WorldToGridSpace(pos, vfPrecision);
        //if we're off the map, return normalized vector toward map center
        if (!mi.IsGridPosOnMap(pos, vfPrecision))
            return float.MaxValue;

        float returnValue = 0;

        //get the nearest ints for both dimensions in both directions
        int xUp = (int)Math.Ceiling(pos.x);
        int xDown = (int)Math.Floor(pos.x);
        int yUp = (int)Math.Ceiling(pos.y);
        int yDown = (int)Math.Floor(pos.y);

        //loop in 2d from the bottom ints to the top
        for (int n = xDown; n <= xUp; n++)
        {
            for (int c = yDown; c <= yUp; c++)
            {
                //get the field vector for the current ints
                float nearVec = cpDistancesSqr[n, c];
                //scale factor, closer is stronger
                float scaleFactor = (1 - Math.Abs(n - pos.x)) * (1 - Math.Abs(c - pos.y));
                //add scaled field vector to return vector
                returnValue += nearVec * scaleFactor;
                //Debug.DrawRay(new Vector2(n, c), nearVec * scaleFactor, Color.blue, Time.deltaTime);
            }
        }
        return returnValue / 4;
    }

    public void QueueVFRecalculation()
    {
        recalculateVF = true;
    }

    void OnMouseOver()
    {
        if (Input.GetMouseButtonDown(0))
        {
            Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            Vector3 newPos = new Vector3(mousePos.x, mousePos.y, ControlPoint.DrawDepth);
            if (mi.IsWorldPosOnMap(newPos))
            {
                ControlPoint newCP = (ControlPoint)(Instantiate(Resources.Load("control point"), newPos, Quaternion.identity) as GameObject).GetComponent(typeof(ControlPoint));
                AddCP(newCP);
            }
        }
        else if (Input.GetMouseButtonDown(1))
        {
            Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            Vector3 newPos = new Vector3(mousePos.x, mousePos.y, ControlPoint.DrawDepth);
            if (mi.IsWorldPosOnMap(newPos))
            {
                ControlPoint newCP = (ControlPoint)(Instantiate(Resources.Load("control point"), newPos, Quaternion.identity) as GameObject).GetComponent(typeof(ControlPoint));
                newCP.Strength = -1;
                StartCoroutine(newCP.SetColor(Color.cyan));
                AddCP(newCP);
            }
        }
    }

    
}
