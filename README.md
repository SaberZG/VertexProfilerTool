# VertexProfilerTool
一个Unity中可用的顶点密度debug工具，部分调试类型可导出密度信息
![image](https://github.com/SaberZG/VertexProfilerTool/assets/74618371/eb9b06ca-6939-4586-bc19-f978bf285367)

支持Build-in和URP管线，无需修改原有shader，即插即用

目前主要开发了五种调试模式：

- OnlyTile模式：将屏幕按照指定的大小切分成多个tile后，统计每个tile的顶点密度，并根据不同的阈值标出
  ![image](https://github.com/SaberZG/VertexProfilerTool/assets/74618371/ceed1db3-d75f-4c69-b508-d87f6569ffb2)
- OnlyMesh模式：逐场景mesh统计顶点数和占用的像素数，计算出密度后根据阈值颜色逐mesh标出
  ![image](https://github.com/SaberZG/VertexProfilerTool/assets/74618371/ae8a25b2-8465-4bf0-bea4-688701f9eb06)
- TileBasedMesh模式：上面两种方法的结合，逐tile逐mesh统计，逐像素标记出密度阈值
  ![image](https://github.com/SaberZG/VertexProfilerTool/assets/74618371/4190f735-36ad-47ea-ab16-bae430da8242)
- MeshHeatMap模式：统计网格顶点的密度热力图（热力图的算法我不是很满意，效果也不够明显，后面有可能重写（todo+1））
  ![image](https://github.com/SaberZG/VertexProfilerTool/assets/74618371/468e7f83-7e67-4015-9763-57355f47e7c2)
- OverDraw模式：查看当前场景Overdraw情况
  ![image](https://github.com/SaberZG/VertexProfilerTool/assets/74618371/d9019392-2953-4475-9123-82ead7cb6c0f)

部分模式可以查看统计信息（Mesh模式可以双击条目定位到目标场景资产），并将统计结果的输出excel：
![image](https://github.com/SaberZG/VertexProfilerTool/assets/74618371/df1db12d-c0bc-4532-bc36-3b4b03552724)

![image](https://github.com/SaberZG/VertexProfilerTool/assets/74618371/cfc7668e-4bf4-4035-aacd-ef17ec742249)
