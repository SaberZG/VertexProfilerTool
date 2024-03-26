using System;
using System.Collections.Generic;
using System.Drawing;
// using System.Drawing.Common;
using System.IO;
using UnityEngine;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using UnityEditor;

namespace VertexProfilerTool
{
    // excel几个使用须知：
    // 1.在EPPluse中，索引都是从1开始的
    // 2.在EPPlus中，Excel文件叫做ExcelPackage，表格叫做ExcelWorkSheet，单元格叫做Cell
    // 3.在1个Excel文件中，有n个表格（sheet），每个表格，有m个单元格（cell），在写入数据的时候需要留意
    public class ProfilerWriter
    {
        private static readonly string folderPath = "Assets/VertexProfiler/ExcelOutput/";
        private static readonly string fileExtend = ".xlsx";
        private static readonly float CellWidth = 64;           // 单元格默认宽度（像素）
        private static readonly float CellHeight = 20;          // 单元格默认高度（像素）
        private const int TexturePercent = 50;      // 图片缩放百分比（整数部分）
        
        private static int TextureOffsetRow = 0;
        private static int TexturePixelLeftOffset = 0;
        private static int SheetTitleOffset = 1;
        private static int SheetLogDataColOffset = 0;
        public static void LogoutToExcel(DisplayType displayType, 
            List<ProfilerDataContents> logoutDataList, 
            RenderTexture screenShot = null, 
            Texture2D screenShotWithGrids = null)
        {
            // 检查文件夹是否存在，如果不存在则创建
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }
            
            string filePath = string.Format("{0}Profiler_{1}_{2}{3}", 
                folderPath, 
                DateTime.Now.ToString("yyyy_M_d_HH_mm_ss"), 
                Time.frameCount, 
                fileExtend);
            
            TextureOffsetRow = 0;
            TexturePixelLeftOffset = 0;
            SheetTitleOffset = 1;
            SheetLogDataColOffset = 0;
            
            FileInfo fileInfo = new FileInfo(filePath);
            using (ExcelPackage excelPackage = new ExcelPackage(fileInfo))
            {
                ExcelWorksheet sheet = excelPackage.Workbook.Worksheets.Add("Sheet1");
                
                // 先计算图片需要占用的区域，调整完单元格的宽度和写入单元格数据之后再插入图片
                // 不然图片的位置会错
                CalculateTextureRowOffset(ref screenShot);
                CalculateTextureRowOffset(ref screenShotWithGrids);
                SheetTitleOffset += TextureOffsetRow + 1;
                // 列表头
                InitSheetHeadsByType(displayType, sheet);
                // 写入数据
                WriteCellDataByDisplayType(displayType, sheet, logoutDataList);
                // 插入图片
                WriteRenderTexture(sheet, ref screenShot);
                WriteRenderTexture(sheet, ref screenShotWithGrids);
                // 保存
                excelPackage.Save();
            }
            // 刷新AssetDatabase，确保新创建的文件在Unity编辑器中可见
            AssetDatabase.Refresh();
            UnityEngine.Object obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(filePath);
            EditorGUIUtility.PingObject(obj);
            // 输出日志
            Debug.Log("Excel file exported to: " + filePath);
        }

        private static void CalculateTextureRowOffset(ref RenderTexture rt, int percent = TexturePercent)
        {
            if (rt == null) return;
            float offsetCol = rt.width * (float)percent / 100f / CellWidth;
            float offsetRow = rt.height * (float)percent / 100f / CellHeight;
            TextureOffsetRow = Mathf.Max(TextureOffsetRow, (int)offsetRow);
        }
        private static void CalculateTextureRowOffset(ref Texture2D tex, int percent = TexturePercent)
        {
            if (tex == null) return;
            float offsetCol = tex.width * (float)percent / 100f / CellWidth;
            float offsetRow = tex.height * (float)percent / 100f / CellHeight;
            TextureOffsetRow = Mathf.Max(TextureOffsetRow, (int)offsetRow);
        }
        
        private static void WriteRenderTexture(ExcelWorksheet sheet, ref RenderTexture rt, int percent = TexturePercent)
        {
            if (rt == null) return;
            
            //将RenderTexture转换为Texture2D
            RenderTexture.active = rt;
            Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            
            //将Texture2D转换为字节流
            byte[] bytes = tex.EncodeToPNG();
            var picture = sheet.Drawings.AddPicture(rt.name, System.Drawing.Image.FromStream(new MemoryStream(bytes)));
            picture.SetPosition(0, TexturePixelLeftOffset);
            picture.SetSize(percent);
            
            TexturePixelLeftOffset = (int)(rt.width * percent / 100f);
        }
        private static void WriteRenderTexture(ExcelWorksheet sheet, ref Texture2D tex, int percent = TexturePercent)
        {
            if (tex == null) return;

            //将Texture2D转换为字节流
            byte[] bytes = tex.EncodeToPNG();
            
            var picture = sheet.Drawings.AddPicture(tex.name, System.Drawing.Image.FromStream(new MemoryStream(bytes)));
            picture.SetPosition(0, TexturePixelLeftOffset);
            picture.SetSize(percent);
            TexturePixelLeftOffset = (int)(tex.width * percent / 100f);
        }

        private static void InitSheetHeadsByType(DisplayType displayType, ExcelWorksheet sheet)
        {
            switch (displayType)
            {
                case DisplayType.OnlyTile:
                    sheet.Cells[SheetTitleOffset, 1 + SheetLogDataColOffset].Value = "Tile Offset";
                    sheet.Cells[SheetTitleOffset, 2 + SheetLogDataColOffset].Value = "顶点数";
                    sheet.Cells[SheetTitleOffset, 3 + SheetLogDataColOffset].Value = "密度（顶点数/1万像素）";
                    // 右移4格，写入排序后的数据
                    SheetLogDataColOffset += 4;
                    sheet.Cells[SheetTitleOffset, 0 + SheetLogDataColOffset].Value = "排序后->";
                    sheet.Cells[SheetTitleOffset, 1 + SheetLogDataColOffset].Value = "Tile Offset";
                    sheet.Cells[SheetTitleOffset, 2 + SheetLogDataColOffset].Value = "顶点数";
                    sheet.Cells[SheetTitleOffset, 3 + SheetLogDataColOffset].Value = "密度（顶点数/1万像素）";
                    // 偏移初始化
                    SheetLogDataColOffset = 0; 
                    break;
                case DisplayType.OnlyMesh:
                    sheet.Cells[SheetTitleOffset, 1 + SheetLogDataColOffset].Value = "资源名称";
                    sheet.Cells[SheetTitleOffset, 2 + SheetLogDataColOffset].Value = "顶点数";
                    sheet.Cells[SheetTitleOffset, 3 + SheetLogDataColOffset].Value = "占用像素数";
                    sheet.Cells[SheetTitleOffset, 4 + SheetLogDataColOffset].Value = "平均像素密度（总顶点数/总占用像素数）";
                    sheet.Cells[SheetTitleOffset, 5 + SheetLogDataColOffset].Value = "资源场景路径";
                    // 偏移初始化
                    SheetLogDataColOffset = 0; 
                    break;
                case DisplayType.TileBasedMesh:
                    sheet.Cells[SheetTitleOffset, 1 + SheetLogDataColOffset].Value = "Tile Offset";
                    sheet.Cells[SheetTitleOffset, 2 + SheetLogDataColOffset].Value = "资源名称";
                    sheet.Cells[SheetTitleOffset, 3 + SheetLogDataColOffset].Value = "棋盘格顶点数";
                    sheet.Cells[SheetTitleOffset, 4 + SheetLogDataColOffset].Value = "网格顶点数（使用率）";
                    sheet.Cells[SheetTitleOffset, 5 + SheetLogDataColOffset].Value = "占用棋盘格像素数";
                    sheet.Cells[SheetTitleOffset, 6 + SheetLogDataColOffset].Value = "平均像素密度（总顶点数/棋盘格占用像素数）";
                    sheet.Cells[SheetTitleOffset, 7 + SheetLogDataColOffset].Value = "资源场景路径";
                    // 偏移初始化
                    SheetLogDataColOffset = 0; 
                    break;
            }
        }

        private static void WriteCellDataByDisplayType(DisplayType displayType, ExcelWorksheet sheet, List<ProfilerDataContents> logoutDataList)
        {
            switch (displayType)
            {
                case DisplayType.OnlyTile:
                    for (int i = 0; i < logoutDataList.Count; i++)
                    {
                        ProfilerDataContents content = logoutDataList[i];
                        WriteCellDataByDisplayType(displayType, sheet, i + 1, content);
                    }

                    // 右移4格，写入排序后的数据
                    SheetLogDataColOffset += 4;
                    logoutDataList.Sort();
                    for (int i = 0; i < logoutDataList.Count; i++)
                    {
                        ProfilerDataContents content = logoutDataList[i];
                        WriteCellDataByDisplayType(displayType, sheet, i + 1, content);
                    }
                    break;
                case DisplayType.OnlyMesh:
                    logoutDataList.Sort();
                    // 设置列宽度，参数是列索引和宽度（以英寸为单位）
                    sheet.Column(1 + SheetLogDataColOffset).Width = 40.0;
                    sheet.Column(5 + SheetLogDataColOffset).Width = 100.0; 
                    for (int i = 0; i < logoutDataList.Count; i++)
                    {
                        ProfilerDataContents content = logoutDataList[i];
                        WriteCellDataByDisplayType(displayType, sheet, i + 1, content);
                    }
                    break;
                case DisplayType.TileBasedMesh:
                    // 设置列宽度，参数是列索引和宽度（以英寸为单位）
                    sheet.Column(2 + SheetLogDataColOffset).Width = 40.0;
                    sheet.Column(7 + SheetLogDataColOffset).Width = 100.0; 
                    for (int i = 0; i < logoutDataList.Count; i++)
                    {
                        ProfilerDataContents content = logoutDataList[i];
                        WriteCellDataByDisplayType(displayType, sheet, i + 1, content);
                    }
                    break;
            }
        }
        private static void WriteCellDataByDisplayType(DisplayType displayType, ExcelWorksheet sheet, int dataIndex, ProfilerDataContents content)
        {
            switch (displayType)
            {
                case DisplayType.OnlyTile:
                    sheet.Cells[SheetTitleOffset + dataIndex, 1 + SheetLogDataColOffset].Value = content.TileIndex;
                    sheet.Cells[SheetTitleOffset + dataIndex, 2 + SheetLogDataColOffset].Value = content.VertexCount;
                    sheet.Cells[SheetTitleOffset + dataIndex, 3 + SheetLogDataColOffset].Value = content.Density2;
                    
                    // 设置单元格颜色
                    sheet.Cells[SheetTitleOffset + dataIndex, 3 + SheetLogDataColOffset].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    
                    System.Drawing.Color c0 = System.Drawing.Color.FromArgb(
                        (int)(content.ProfilerColor.a * 255f),
                        (int)(content.ProfilerColor.r * 255f),
                        (int)(content.ProfilerColor.g * 255f),
                        (int)(content.ProfilerColor.b * 255f));

                    sheet.Cells[SheetTitleOffset + dataIndex, 3 + SheetLogDataColOffset].Style.Fill.BackgroundColor.SetColor(c0);
                    break;
                case DisplayType.OnlyMesh:
                    sheet.Cells[SheetTitleOffset + dataIndex, 1 + SheetLogDataColOffset].Value = content.ResourceName;
                    sheet.Cells[SheetTitleOffset + dataIndex, 2 + SheetLogDataColOffset].Value = content.VertexCount;
                    sheet.Cells[SheetTitleOffset + dataIndex, 3 + SheetLogDataColOffset].Value = content.PixelCount;
                    if (content.Density_float == float.MaxValue)
                    {
                        sheet.Cells[SheetTitleOffset + dataIndex, 4 + SheetLogDataColOffset].Value = "无像素占用";
                    }
                    else
                    {
                        sheet.Cells[SheetTitleOffset + dataIndex, 4 + SheetLogDataColOffset].Value = content.Density;
                    }
                    sheet.Cells[SheetTitleOffset + dataIndex, 5 + SheetLogDataColOffset].Value = content.RendererHierarchyPath;
                    
                    // 设置单元格颜色
                    sheet.Cells[SheetTitleOffset + dataIndex, 4 + SheetLogDataColOffset].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    System.Drawing.Color c1 = System.Drawing.Color.FromArgb(
                        (int)(content.ProfilerColor.a * 255f),
                        (int)(content.ProfilerColor.r * 255f),
                        (int)(content.ProfilerColor.g * 255f),
                        (int)(content.ProfilerColor.b * 255f));

                    sheet.Cells[SheetTitleOffset + dataIndex, 4 + SheetLogDataColOffset].Style.Fill.BackgroundColor.SetColor(c1);
                    break;
                case DisplayType.TileBasedMesh:
                    // 是否是tile的分割节点，Mesh信息需要缩进处理
                    bool isTileIndexContent = content.HasData(content.TileIndex);
                    int dent = isTileIndexContent ? 0 : 1;
                    if (isTileIndexContent)
                    {
                        sheet.Cells[SheetTitleOffset + dataIndex, 1 + SheetLogDataColOffset + dent].Value = content.TileIndex;
                    }
                    else
                    {
                        sheet.Cells[SheetTitleOffset + dataIndex, 1 + SheetLogDataColOffset + dent].Value = content.ResourceName;
                        sheet.Cells[SheetTitleOffset + dataIndex, 2 + SheetLogDataColOffset + dent].Value = content.VertexCount;
                        sheet.Cells[SheetTitleOffset + dataIndex, 3 + SheetLogDataColOffset + dent].Value = content.VertexInfo;
                        sheet.Cells[SheetTitleOffset + dataIndex, 4 + SheetLogDataColOffset + dent].Value = content.PixelCount;
                        if (content.Density_float == float.MaxValue)
                        {
                            sheet.Cells[SheetTitleOffset + dataIndex, 5 + SheetLogDataColOffset + dent].Value = "无像素占用";
                        }
                        else
                        {
                            sheet.Cells[SheetTitleOffset + dataIndex, 5 + SheetLogDataColOffset + dent].Value = content.Density;
                        }
                        sheet.Cells[SheetTitleOffset + dataIndex, 6 + SheetLogDataColOffset + dent].Value = content.RendererHierarchyPath;
                        
                        // 设置单元格颜色
                        sheet.Cells[SheetTitleOffset + dataIndex, 5 + SheetLogDataColOffset + dent].Style.Fill.PatternType = ExcelFillStyle.Solid;
                        System.Drawing.Color c2 = System.Drawing.Color.FromArgb(
                            (int)(content.ProfilerColor.a * 255f),
                            (int)(content.ProfilerColor.r * 255f),
                            (int)(content.ProfilerColor.g * 255f),
                            (int)(content.ProfilerColor.b * 255f));

                        sheet.Cells[SheetTitleOffset + dataIndex, 5 + SheetLogDataColOffset + dent].Style.Fill.BackgroundColor.SetColor(c2);
                    }
                    break;
            }
        }
    }
}
