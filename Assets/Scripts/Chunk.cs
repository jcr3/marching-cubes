using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Chunk
{

    float[,,] vertexMap;

    public GameObject chunkObject;

    MeshFilter meshFilter;
    MeshRenderer meshRenderer;
    MeshCollider meshCollider;

    Vector3Int chunkPosition;

    //lists hold vertices and triangles of mesh
    readonly List<Vector3> vertices = new List<Vector3>();
    readonly List<int> triangles = new List<int>();

    //create fast noise object
    FastNoise myNoise = new FastNoise();

    //constructor for chunk object
    public Chunk(Vector3Int _position)
    {

        //creating new game object for chunk
        chunkObject = new GameObject();
        chunkObject.name = string.Format("Chunk {0}, {1}", _position.x, _position.y);
        chunkPosition = _position;
        chunkObject.transform.position = chunkPosition;

        //adding and assigning the components
        meshFilter = chunkObject.AddComponent<MeshFilter>();
        meshRenderer = chunkObject.AddComponent<MeshRenderer>();
        meshCollider = chunkObject.AddComponent<MeshCollider>();

        //assigning material to mesh renderer
        meshRenderer.material = Resources.Load<Material>("Materials/Dirt");


        //setting up noise object
        myNoise.SetSeed(GameData.seed);
        myNoise.SetFrequency(GameData.frequency);
        myNoise.SetNoiseType(FastNoise.NoiseType.SimplexFractal);

        //always one more vertex than cubes in a row
        vertexMap = new float[GameData.ChunkWidth + 1, GameData.ChunkHeight + 1, GameData.ChunkWidth + 1];

        PopulateVertexMap();
        MarchCubes();

    }

    //remap value from A to B range to C to D range
    float Remap(float value, float A, float B, float C, float D)
    {
        return (value - A) / (B - A) * (D - C) + C;
    }

    void PopulateVertexMap()
    {

        //loop through every vertex position (one more vertex than cubes)
        //starting moving upwards (x, then z, then y)
        for (int y = 0; y < GameData.ChunkHeight + 1; y++)
        {
            for (int z = 0; z < GameData.ChunkWidth + 1; z++)
            {
                for (int x = 0; x < GameData.ChunkWidth + 1; x++)
                {
                    //getting noise and converting from range -1 to 1 to 0 to 1
                    float noise = GameData.amplitude * (myNoise.GetNoise(x + chunkPosition.x, z + chunkPosition.z, y + chunkPosition.y) + 1) / 2;

                    //add a floor
                    if (y == 1)
                        vertexMap[x, y, z] = GameData.surfaceLevel + 0.01f;
                    //moving upwards, fade from full blocks into caves, then back to full blocks for surface, then air
                    //y is less than min cave height so multiply noise by gradient
                    //of cave fade intensity to 1 to fade in noise from cave fade intensity
                    else if (y < GameData.caveFadeIn)
                    {
                        float gradient = Remap(y, 0f, GameData.caveFadeIn, GameData.caveFadeIntensity, 1f);
                        vertexMap[x, y, z] = noise * gradient;
                    }
                    //normal noise
                    else if (y < GameData.caveFadeOut)
                    {
                        vertexMap[x, y, z] = noise;
                    }
                    //fade noise towards cave fade intensity
                    else if (y < GameData.groundFadeIn)
                    {
                        float gradient = Remap(y, GameData.caveFadeOut, GameData.groundFadeIn, 1f, GameData.caveFadeIntensity);
                        vertexMap[x, y, z] = noise * gradient;
                    }
                    //fade noise from cave fade intensity to 0
                    else
                    {
                        float gradient = Remap(y, GameData.groundFadeIn, GameData.groundFadeOut, GameData.caveFadeIntensity, 0f);
                        vertexMap[x, y, z] = noise * gradient;
                    }
                }
            }
        }
    }

    float FindVertexValue(Vector3Int position)
    {
        //so finding vertex value is easier
        return vertexMap[position.x, position.y, position.z];
    }

    int CalcConfiguration (float[] cornerValue)
    {
        //repeat for each corner of a cube
        int configurationIndex = 0;
        for (int i = 0; i < 8; i++)
        {
            //if the value is less than the surface level mask the bit 1 into the configuration index
            //for example if corners 0, 2 and 7 are below surface level the index 10000101 (msb first)
            //256 possilbe configurations (00000000 to 11111111)
            if (cornerValue[i] < GameData.surfaceLevel)
                configurationIndex |= 1 << i;
        }

        return configurationIndex;

    }

    int VertForIndex (Vector3 vert)
    {
        //loop through all vertices in the list and if vert is found
        //return it to the triangle
        for (int i = 0; i < vertices.Count; i++)
        {

            if (vertices[i] == vert)
                return i;

        }

        //add new vert and triangle
        vertices.Add(vert);
        return vertices.Count - 1;
    }

    void GenCubeMesh(Vector3Int position)
    {
        //find vertex values for each corner of cube at a position
        //using the table of relative corner positions
        float[] cornerValue = new float[8];
        for (int i = 0; i < 8; i++)
        {

            cornerValue[i] = FindVertexValue(position + GameData.cornerTable[i]);

        }

        //calculate the configuration index of the cube
        int configIndex = CalcConfiguration(cornerValue);

        //these configurations are either completely outside or inside the mesh
        //so no triagnles
        if (configIndex == 0 || configIndex == 255)
            return;

        //loop through the triangles and their vertices of the configuration
        //max 5 triangles with 3 vertices each
        int edgeIndex = 0;
        for (int t = 0; t < 5; t++)
        {
            for (int v = 0; v < 3; v++)
            {

                //find the edge indices which the vertices need to be placed upon
                //to form the triangles of the configuration
                int index = GameData.triangleTable[configIndex, edgeIndex];

                //-1 means no more indices
                if (index == -1)
                    return;

                //find the position of the two corners' of each edge
                Vector3 corner1 = position + GameData.cornerTable[GameData.edgeIndices[index, 0]];
                Vector3 corner2 = position + GameData.cornerTable[GameData.edgeIndices[index, 1]];

                Vector3 vertPosition;

                if (GameData.smooth)
                {
                    //find the values stored at each corner and if they're greater than 1
                    //set them equal to 1
                    float value1 = cornerValue[GameData.edgeIndices[index, 0]];
                    if (value1 > 1f)
                        value1 = 1f;
                    float value2 = cornerValue[GameData.edgeIndices[index, 1]];
                    if (value2 > 1f)
                        value2 = 1f;

                    //interpolate between the corner values to estimate
                    //where along the edge equals the surfaceLevel
                    float lerp = (GameData.surfaceLevel - value1) / (value2 - value1);
                    vertPosition = corner1 + lerp * (corner2 - corner1);

                }
                else
                    //find the midpoint of the two corners
                    vertPosition = (corner1 + corner2) / 2f;

                if (GameData.flatShaded)
                {
                    //add the vertices and triangles to the lists
                    vertices.Add(vertPosition);
                    triangles.Add(vertices.Count - 1);
                }
                else
                    triangles.Add(VertForIndex(vertPosition));

                edgeIndex++;

            }
        }
    }

    void BuildMesh()
    {
        //create a mesh and assign it vertices and triangles from the lists
        Mesh mesh = new Mesh();
        mesh.vertices = vertices.ToArray();
        mesh.triangles = triangles.ToArray();

        //recalulate normals so the mesh is facing the right direction
        mesh.RecalculateNormals();

        //assign the meshfilter and meshcolider to this mesh
        meshFilter.mesh = mesh;
        meshCollider.sharedMesh = mesh;
    }

    void ClearMeshData()
    {
        //clear verticies and triangles
        vertices.Clear();
        triangles.Clear();
    }

    void MarchCubes()
    {
        ClearMeshData();

        //march through each cube
        for (int x = 0; x < GameData.ChunkWidth; x++)
        {
            for (int y = 0; y < GameData.ChunkHeight; y++)
            {
                for (int z = 0; z < GameData.ChunkWidth; z++)
                {
                    //create the cubes' mesh according to the position
                    GenCubeMesh(new Vector3Int(x, y, z));

                }
            }
        }

        BuildMesh();

    }

}
