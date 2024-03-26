using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.UI;
using Unity.VisualScripting;
using UnityEngine;

namespace VertexProfilerTool
{
    public class UITile : MonoBehaviour
    {
        public RectTransform rect;
        public Text txtTileIndex;

        public void SetData(int tileWidth, int tileHeight, int tileNumX, int tileIndex)
        {
            transform.name = "UITile" + tileIndex;
            
            rect.sizeDelta = new Vector2(tileWidth, tileHeight);
            int tilePosY = tileIndex / tileNumX;
            int tilePosX = tileIndex - tilePosY * tileNumX;
            rect.anchoredPosition = new Vector2(tilePosX * tileWidth, tilePosY * tileHeight);
            txtTileIndex.text = tileIndex.ToString();
        }

        public void SetActive(bool b)
        {
            gameObject.SetActive(b);
        }

        public void SetTileNumActive(bool b)
        {
            txtTileIndex.gameObject.SetActive(b);
        }
    }
}
