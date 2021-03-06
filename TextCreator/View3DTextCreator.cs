﻿/*
Copyright (c) 2014, Lars Brubaker
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met: 

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer. 
2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution. 

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

The views and conclusions contained in the software and documentation are those
of the authors and should not be interpreted as representing official policies, 
either expressed or implied, of the FreeBSD Project.
*/

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Threading;


using MatterHackers.Agg;
using MatterHackers.Agg.Font;
using MatterHackers.Agg.UI;
using MatterHackers.MatterControl.DataStorage;
using MatterHackers.MatterControl.PartPreviewWindow;
using MatterHackers.MatterControl.PrintLibrary;
using MatterHackers.MatterControl.PrintQueue;
using MatterHackers.MeshVisualizer;
using MatterHackers.PolygonMesh;
using MatterHackers.PolygonMesh.Processors;
using MatterHackers.RayTracer;
using MatterHackers.RayTracer.Traceable;
using MatterHackers.RenderOpenGl;
using MatterHackers.VectorMath;

namespace MatterHackers.MatterControl.Plugins.TextCreator
{
    public class View3DTextCreator : PartPreview3DWidget
    {
        Slider spacingScrollBar;
        Slider sizeScrollBar;
        Slider heightScrollBar;
        
        CheckBox createUnderline;

        double lastHeightValue = 1;
        double lastSizeValue = 1;

        ProgressControl processingProgressControl;
        FlowLayoutWidget editPlateButtonsContainer;

        Button saveButton;
        Button saveAndExitButton;
        Button closeButton;
        String word;

        List<Mesh> asynchMeshesList = new List<Mesh>();
        List<ScaleRotateTranslate> asynchMeshTransforms = new List<ScaleRotateTranslate>();
        List<PlatingMeshData> asynchPlatingDataList = new List<PlatingMeshData>();

        List<PlatingMeshData> MeshPlatingData;

        public ScaleRotateTranslate SelectedMeshTransform
        {
            get { return meshViewerWidget.SelectedMeshTransform; }
            set { meshViewerWidget.SelectedMeshTransform = value; }
        }

        public Mesh SelectedMesh
        {
            get { return meshViewerWidget.SelectedMesh; }
        }

        public int SelectedMeshIndex
        {
            get { return meshViewerWidget.SelectedMeshIndex; }
            set { meshViewerWidget.SelectedMeshIndex = value; }
        }

        public List<Mesh> Meshes
        {
            get { return meshViewerWidget.Meshes; }
        }

        public List<ScaleRotateTranslate> MeshTransforms
        {
            get { return meshViewerWidget.MeshTransforms; }
        }

        internal struct MeshSelectInfo
        {
            internal bool downOnPart;
            internal PlaneShape hitPlane;
            internal Vector3 planeDownHitPos;
            internal Vector3 lastMoveDelta;
        }

        TypeFace boldTypeFace;
        public View3DTextCreator(Vector3 viewerVolume, Vector2 bedCenter, MeshViewerWidget.BedShape bedShape)
        {
            string staticDataPath = DataStorage.ApplicationDataStorage.Instance.ApplicationStaticDataPath;
            string fontPath = Path.Combine(staticDataPath, "Fonts", "LiberationSans-Bold.svg");
            boldTypeFace = TypeFace.LoadSVG(fontPath);

            MeshPlatingData = new List<PlatingMeshData>();

            FlowLayoutWidget mainContainerTopToBottom = new FlowLayoutWidget(FlowDirection.TopToBottom);
            mainContainerTopToBottom.HAnchor = Agg.UI.HAnchor.Max_FitToChildren_ParentWidth;
            mainContainerTopToBottom.VAnchor = Agg.UI.VAnchor.Max_FitToChildren_ParentHeight;

            FlowLayoutWidget centerPartPreviewAndControls = new FlowLayoutWidget(FlowDirection.LeftToRight);
            centerPartPreviewAndControls.AnchorAll();

            GuiWidget viewArea = new GuiWidget();
            viewArea.AnchorAll();
            {
                meshViewerWidget = new MeshViewerWidget(viewerVolume, bedCenter, bedShape);
                meshViewerWidget.AlwaysRenderBed = true;
                meshViewerWidget.AnchorAll();
            }
            viewArea.AddChild(meshViewerWidget);

            centerPartPreviewAndControls.AddChild(viewArea);
            mainContainerTopToBottom.AddChild(centerPartPreviewAndControls);

            FlowLayoutWidget buttonBottomPanel = new FlowLayoutWidget(FlowDirection.LeftToRight);
            buttonBottomPanel.HAnchor = HAnchor.ParentLeftRight;
            buttonBottomPanel.Padding = new BorderDouble(3, 3);
            buttonBottomPanel.BackgroundColor = ActiveTheme.Instance.PrimaryBackgroundColor;

            buttonRightPanel = CreateRightButtonPanel(viewerVolume.y);

            // add in the plater tools
            {
                FlowLayoutWidget editToolBar = new FlowLayoutWidget();

                processingProgressControl = new ProgressControl("Finding Parts:", ActiveTheme.Instance.PrimaryTextColor, ActiveTheme.Instance.PrimaryAccentColor);
                processingProgressControl.VAnchor = Agg.UI.VAnchor.ParentCenter;
                editToolBar.AddChild(processingProgressControl);
                editToolBar.VAnchor |= Agg.UI.VAnchor.ParentCenter;

                editPlateButtonsContainer = new FlowLayoutWidget();

                MHTextEditWidget textToAddWidget = new MHTextEditWidget("", pixelWidth: 300, messageWhenEmptyAndNotSelected: "Enter Text Here");
                textToAddWidget.VAnchor = VAnchor.ParentCenter;
                textToAddWidget.Margin = new BorderDouble(5);
                editPlateButtonsContainer.AddChild(textToAddWidget);
                textToAddWidget.ActualTextEditWidget.EnterPressed += (object sender, KeyEventArgs keyEvent) =>
                {
                    InsertTextNow(textToAddWidget.Text);
                };

                Button insertTextButton = textImageButtonFactory.Generate("Insert");
                editPlateButtonsContainer.AddChild(insertTextButton);
                insertTextButton.Click += (sender, e) =>
                {
                    InsertTextNow(textToAddWidget.Text);
                };

                KeyDown += (sender, e) =>
                {
                    KeyEventArgs keyEvent = e as KeyEventArgs;
                    if (keyEvent != null && !keyEvent.Handled)
                    {
                        if (keyEvent.KeyCode == Keys.Escape)
                        {
                            if (meshSelectInfo.downOnPart)
                            {
                                meshSelectInfo.downOnPart = false;

                                ScaleRotateTranslate translated = SelectedMeshTransform;
                                translated.translation *= transformOnMouseDown;
                                SelectedMeshTransform = translated;

                                Invalidate();
                            }
                        }
                    }
                };

                editToolBar.AddChild(editPlateButtonsContainer);
                buttonBottomPanel.AddChild(editToolBar);
            }

            GuiWidget buttonRightPanelHolder = new GuiWidget(HAnchor.FitToChildren, VAnchor.ParentBottomTop);
            centerPartPreviewAndControls.AddChild(buttonRightPanelHolder);
            buttonRightPanelHolder.AddChild(buttonRightPanel);

            viewControls3D = new ViewControls3D(meshViewerWidget);

            buttonRightPanelDisabledCover = new Cover(HAnchor.ParentLeftRight, VAnchor.ParentBottomTop);
            buttonRightPanelDisabledCover.BackgroundColor = new RGBA_Bytes(ActiveTheme.Instance.PrimaryBackgroundColor, 150);
            buttonRightPanelHolder.AddChild(buttonRightPanelDisabledCover);
            LockEditControls();

            GuiWidget leftRightSpacer = new GuiWidget();
            leftRightSpacer.HAnchor = HAnchor.ParentLeftRight;
            buttonBottomPanel.AddChild(leftRightSpacer);

            closeButton = textImageButtonFactory.Generate("Close");
            buttonBottomPanel.AddChild(closeButton);

            mainContainerTopToBottom.AddChild(buttonBottomPanel);

            this.AddChild(mainContainerTopToBottom);
            this.AnchorAll();

            meshViewerWidget.TrackballTumbleWidget.TransformState = TrackBallController.MouseDownType.Rotation;

            AddChild(viewControls3D);

            // set the view to be a good angle and distance
            meshViewerWidget.TrackballTumbleWidget.TrackBallController.Scale = .06;
            meshViewerWidget.TrackballTumbleWidget.TrackBallController.Rotate(Quaternion.FromEulerAngles(new Vector3(-MathHelper.Tau * .02, 0, 0)));

            AddHandlers();
            UnlockEditControls();
            // but make sure we can't use the right panel yet
            buttonRightPanelDisabledCover.Visible = true;
        }

        private void InsertTextNow(string text)
        {
            if (text.Length > 0)
            {
                this.word = text;
                ResetWordLayoutSettings();
                processingProgressControl.textWidget.Text = "Inserting Text";
                processingProgressControl.Visible = true;
                processingProgressControl.PercentComplete = 0;
                LockEditControls();

                BackgroundWorker insertTextBackgroundWorker = null;
                insertTextBackgroundWorker = new BackgroundWorker();
                insertTextBackgroundWorker.WorkerReportsProgress = true;

                insertTextBackgroundWorker.DoWork += new DoWorkEventHandler(insertTextBackgroundWorker_DoWork);
                insertTextBackgroundWorker.ProgressChanged += new ProgressChangedEventHandler(BackgroundWorker_ProgressChanged);
                insertTextBackgroundWorker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(insertTextBackgroundWorker_RunWorkerCompleted);

                insertTextBackgroundWorker.RunWorkerAsync(text);
            }
        }

        private void ResetWordLayoutSettings()
        {
            spacingScrollBar.Value = 1;
            sizeScrollBar.Value = 1;
            heightScrollBar.Value = .25;
            lastHeightValue = 1;
            lastSizeValue = 1;
        }

        private bool FindMeshHitPosition(Vector2 screenPosition, out int meshHitIndex)
        {
            meshHitIndex = 0;
            if (MeshPlatingData.Count == 0 || MeshPlatingData[0].traceableData == null)
            {
                return false;
            }

            List<IRayTraceable> mesheTraceables = new List<IRayTraceable>();
            for (int i = 0; i < MeshPlatingData.Count; i++)
            {
                mesheTraceables.Add(new Transform(MeshPlatingData[i].traceableData, MeshTransforms[i].TotalTransform));
            }
            IRayTraceable allObjects = BoundingVolumeHierarchy.CreateNewHierachy(mesheTraceables);

            Ray ray = meshViewerWidget.TrackballTumbleWidget.LastScreenRay;
            IntersectInfo info = allObjects.GetClosestIntersection(ray);
            if (info != null)
            {
                meshSelectInfo.planeDownHitPos = info.hitPosition;
                meshSelectInfo.lastMoveDelta = new Vector3();

                for (int i = 0; i < MeshPlatingData.Count; i++)
                {
                    List<IRayTraceable> insideBounds = new List<IRayTraceable>();
                    MeshPlatingData[i].traceableData.GetContained(insideBounds, info.closestHitObject.GetAxisAlignedBoundingBox());
                    if (insideBounds.Contains(info.closestHitObject))
                    {
                        meshHitIndex = i;
                        return true;
                    }
                }
            }

            return false;
        }

        Matrix4X4 transformOnMouseDown = Matrix4X4.Identity;
        MeshSelectInfo meshSelectInfo;
        public override void OnMouseDown(MouseEventArgs mouseEvent)
        {
            base.OnMouseDown(mouseEvent);
            if (meshViewerWidget.TrackballTumbleWidget.UnderMouseState == Agg.UI.UnderMouseState.FirstUnderMouse)
            {
                if (meshViewerWidget.TrackballTumbleWidget.TransformState == TrackBallController.MouseDownType.None)
                {
                    viewControls3D.partSelectButton.ClickButton(null);
                    int meshHitIndex;
                    if (FindMeshHitPosition(mouseEvent.Position, out meshHitIndex))
                    {
                        meshSelectInfo.hitPlane = new PlaneShape(Vector3.UnitZ, meshSelectInfo.planeDownHitPos.z, null);
                        SelectedMeshIndex = meshHitIndex;
                        transformOnMouseDown = SelectedMeshTransform.translation;
                        Invalidate();
                        meshSelectInfo.downOnPart = true;
                    }
                }
            }
        }

        public override void OnDraw(Graphics2D graphics2D)
        {
            //DoCsgTest();
            base.OnDraw(graphics2D);
        }

        public override void OnMouseMove(MouseEventArgs mouseEvent)
        {
            if (meshViewerWidget.TrackballTumbleWidget.TransformState == TrackBallController.MouseDownType.None && meshSelectInfo.downOnPart)
            {
                Ray ray = meshViewerWidget.TrackballTumbleWidget.LastScreenRay;
                IntersectInfo info = meshSelectInfo.hitPlane.GetClosestIntersection(ray);
                if (info != null)
                {
                    Vector3 delta = info.hitPosition - meshSelectInfo.planeDownHitPos;

                    Matrix4X4 totalTransfrom = Matrix4X4.CreateTranslation(new Vector3(-meshSelectInfo.lastMoveDelta));
                    totalTransfrom *= Matrix4X4.CreateTranslation(new Vector3(delta));
                    meshSelectInfo.lastMoveDelta = delta;


                    ScaleRotateTranslate translated = SelectedMeshTransform;
                    translated.translation *= totalTransfrom;
                    SelectedMeshTransform = translated;

                    Invalidate();
                }
            }

            base.OnMouseMove(mouseEvent);
        }

        public override void OnMouseUp(MouseEventArgs mouseEvent)
        {
            if (meshViewerWidget.TrackballTumbleWidget.TransformState == TrackBallController.MouseDownType.None
                && meshSelectInfo.downOnPart
                && meshSelectInfo.lastMoveDelta != Vector3.Zero)
            {
                saveButton.Visible = true;
                saveAndExitButton.Visible = true;
            }

            meshSelectInfo.downOnPart = false;

            base.OnMouseUp(mouseEvent);
        }

        void insertTextBackgroundWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            BackgroundWorker backgroundWorker = (BackgroundWorker)sender;

            asynchMeshesList.Clear();
            asynchMeshTransforms.Clear();
            asynchPlatingDataList.Clear();

            string currentText = (string)e.Argument;
            TypeFacePrinter printer = new TypeFacePrinter(currentText, new StyledTypeFace(boldTypeFace, 12));
            Vector2 size = printer.GetSize(currentText);
            double centerOffset = -size.x / 2;

            for (int i = 0; i < currentText.Length; i++)
            {
                int newIndex = asynchMeshesList.Count;

                TypeFacePrinter letterPrinter = new TypeFacePrinter(currentText[i].ToString(), new StyledTypeFace(boldTypeFace, 12));
                Mesh textMesh = VertexSourceToMesh.Extrude(letterPrinter, 10 + (i%2));

                if (textMesh.Faces.Count > 0)
                {
                    asynchMeshesList.Add(textMesh);

                    PlatingMeshData newMeshInfo = new PlatingMeshData();

                    newMeshInfo.xSpacing = printer.GetOffsetLeftOfCharacterIndex(i).x + centerOffset;
                    asynchPlatingDataList.Add(newMeshInfo);
                    asynchMeshTransforms.Add(ScaleRotateTranslate.Identity());

                    PlatingHelper.CreateITraceableForMesh(asynchPlatingDataList, asynchMeshesList, newIndex);

                    PlatingHelper.PlaceMeshOnBed(asynchMeshesList, asynchMeshTransforms, newIndex, false);
                }

                backgroundWorker.ReportProgress((i + 1) * 95 / currentText.Length);
            }


            SetWordSpacing(asynchMeshesList, asynchMeshTransforms, asynchPlatingDataList);
            SetWordSize(asynchMeshesList, asynchMeshTransforms);
            SetWordHeight(asynchMeshesList, asynchMeshTransforms);

            if (createUnderline.Checked)
            {
                CreateUnderline(asynchMeshesList, asynchMeshTransforms, asynchPlatingDataList);
            }

            backgroundWorker.ReportProgress(95);
        }

        void insertTextBackgroundWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            UnlockEditControls();
            PullMeshDataFromAsynchLists();
            saveButton.Visible = true;
            saveAndExitButton.Visible = true;
            // now set the selection to the new copy
            SelectedMeshIndex = 0;
        }

        private void CreateUnderline(List<Mesh> meshesList, List<ScaleRotateTranslate> meshTransforms, List<PlatingMeshData> platingDataList)
        {
            if (meshesList.Count > 0)
            {
                AxisAlignedBoundingBox bounds = meshesList[0].GetAxisAlignedBoundingBox(meshTransforms[0].TotalTransform);
                for (int i = 1; i < meshesList.Count; i++)
                {
                    bounds = AxisAlignedBoundingBox.Union(bounds, meshesList[i].GetAxisAlignedBoundingBox(meshTransforms[i].TotalTransform));
                }

                double xSize = bounds.XSize;
                double ySize = bounds.YSize / 5;
                double zSize = bounds.ZSize / 3;
                Mesh connectionLine = PlatonicSolids.CreateCube(xSize, ySize, zSize);
                meshesList.Add(connectionLine);
                platingDataList.Add(new PlatingMeshData());
                meshTransforms.Add(ScaleRotateTranslate.CreateTranslation((bounds.maxXYZ.x + bounds.minXYZ.x) / 2, ySize / 2 - ySize * 2 / 3, zSize / 2));
                PlatingHelper.CreateITraceableForMesh(platingDataList, meshesList, meshesList.Count - 1);
            }
        }

        private void PushMeshDataToAsynchLists(bool copyTraceInfo)
        {
            asynchMeshesList.Clear();
            asynchMeshTransforms.Clear();
            for (int i = 0; i < Meshes.Count; i++)
            {
                Mesh mesh = Meshes[i];
                asynchMeshesList.Add(new Mesh(mesh));
                asynchMeshTransforms.Add(MeshTransforms[i]);
            }
            asynchPlatingDataList.Clear();
            for (int i = 0; i < MeshPlatingData.Count; i++)
            {
                PlatingMeshData meshData = new PlatingMeshData();
                meshData.currentScale = MeshPlatingData[i].currentScale;
                if (copyTraceInfo)
                {
                    meshData.traceableData = MeshPlatingData[i].traceableData;
                }
                asynchPlatingDataList.Add(meshData);
            }
        }

        void arrangePartsBackgroundWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            UnlockEditControls();
            saveButton.Visible = true;
            saveAndExitButton.Visible = true;
            viewControls3D.partSelectButton.ClickButton(null);

            PullMeshDataFromAsynchLists();
        }

        private void PullMeshDataFromAsynchLists()
        {
            Meshes.Clear();
            foreach (Mesh mesh in asynchMeshesList)
            {
                Meshes.Add(mesh);
            }
            MeshTransforms.Clear();
            foreach (ScaleRotateTranslate transform in asynchMeshTransforms)
            {
                MeshTransforms.Add(transform);
            }
            MeshPlatingData.Clear();
            foreach (PlatingMeshData meshData in asynchPlatingDataList)
            {
                MeshPlatingData.Add(meshData);
            }
        }

        void meshViewerWidget_LoadDone(object sender, EventArgs e)
        {
            UnlockEditControls();
        }

        void LockEditControls()
        {
            editPlateButtonsContainer.Visible = false;
            buttonRightPanelDisabledCover.Visible = true;

            viewControls3D.PartSelectVisible = false;
            if (meshViewerWidget.TrackballTumbleWidget.TransformState == TrackBallController.MouseDownType.None)
            {
                viewControls3D.rotateButton.ClickButton(null);
            }
        }

        void UnlockEditControls()
        {
            buttonRightPanelDisabledCover.Visible = false;
            processingProgressControl.Visible = false;

            viewControls3D.PartSelectVisible = true;
            editPlateButtonsContainer.Visible = true;
        }

        private void DeleteSelectedMesh()
        {
            // don't ever delet the last mesh
            if (Meshes.Count > 1)
            {
                Meshes.RemoveAt(SelectedMeshIndex);
                MeshPlatingData.RemoveAt(SelectedMeshIndex);
                MeshTransforms.RemoveAt(SelectedMeshIndex);
                SelectedMeshIndex = Math.Min(SelectedMeshIndex, Meshes.Count - 1);
                saveButton.Visible = true;
                saveAndExitButton.Visible = true;
                Invalidate();
            }
        }

        void BackgroundWorker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            processingProgressControl.PercentComplete = e.ProgressPercentage;
        }

        private FlowLayoutWidget CreateRightButtonPanel(double buildHeight)
        {
            FlowLayoutWidget buttonRightPanel = new FlowLayoutWidget(FlowDirection.TopToBottom);
            buttonRightPanel.Width = 200;
            {
                BorderDouble buttonMargin = new BorderDouble(top: 3);

                // put in the word editing menu
                {
                    CheckBox expandWordOptions = expandMenuOptionFactory.GenerateCheckBoxButton("Word Edit", "icon_arrow_right_no_border_32x32.png", "icon_arrow_down_no_border_32x32.png");
                    expandWordOptions.Margin = new BorderDouble(bottom: 2);
                    buttonRightPanel.AddChild(expandWordOptions);

                    FlowLayoutWidget wordOptionContainer = new FlowLayoutWidget(FlowDirection.TopToBottom);
                    wordOptionContainer.HAnchor = HAnchor.ParentLeftRight;
                    wordOptionContainer.Visible = false;
                    buttonRightPanel.AddChild(wordOptionContainer);

                    spacingScrollBar = InseretUiForSlider(wordOptionContainer, "Spacing:", .5, 1);
                    {
                        spacingScrollBar.ValueChanged += (sender, e) =>
                        {
                            SetWordSpacing(Meshes, MeshTransforms, MeshPlatingData);
                            RebuildUnderlineIfRequired();
                        };
                    }

                    sizeScrollBar = InseretUiForSlider(wordOptionContainer, "Size:", .3, 2);
                    {
                        sizeScrollBar.ValueChanged += (sender, e) =>
                        {
                            SetWordSize(Meshes, MeshTransforms);

                            SetWordSpacing(Meshes, MeshTransforms, MeshPlatingData);
                            RebuildUnderlineIfRequired();
                        };
                    }

                    heightScrollBar = InseretUiForSlider(wordOptionContainer, "Height:", .05, 1);
                    {
                        heightScrollBar.ValueChanged += (sender, e) =>
                        {
                            SetWordHeight(Meshes, MeshTransforms);
                            RebuildUnderlineIfRequired();
                        };
                    }
                       
                    createUnderline = new CheckBox(new CheckBoxViewText("Underline", textColor: ActiveTheme.Instance.PrimaryTextColor));
                    createUnderline.Checked = true;
                    createUnderline.Margin = new BorderDouble(10, 5);
                    createUnderline.HAnchor = HAnchor.ParentLeft;
                    wordOptionContainer.AddChild(createUnderline);
                    createUnderline.CheckedStateChanged += (sender, e) =>
                    {
                        int oldIndex = SelectedMeshIndex;
                        if (!createUnderline.Checked)
                        {
                            // we need to remove the underline
                            if (Meshes.Count > 1)
                            {
                                SelectedMeshIndex = Meshes.Count - 1;
                                DeleteSelectedMesh();
                            }
                        }
                        else if (Meshes.Count > 0)
                        {
                            // we need to add the underline
                            CreateUnderline(Meshes, MeshTransforms, MeshPlatingData);
                        }
                        SelectedMeshIndex = oldIndex;
                    };

                    expandWordOptions.CheckedStateChanged += (sender, e) =>
                    {
                        wordOptionContainer.Visible = expandWordOptions.Checked;
                    };

                    expandWordOptions.Checked = true;
                }

                // put in the letter editing menu
                {
                    CheckBox expandLetterOptions = expandMenuOptionFactory.GenerateCheckBoxButton("Letter", "icon_arrow_right_no_border_32x32.png", "icon_arrow_down_no_border_32x32.png");
                    expandLetterOptions.Margin = new BorderDouble(bottom: 2);
                    //buttonRightPanel.AddChild(expandLetterOptions);

                    FlowLayoutWidget letterOptionContainer = new FlowLayoutWidget(FlowDirection.TopToBottom);
                    letterOptionContainer.HAnchor = HAnchor.ParentLeftRight;
                    letterOptionContainer.Visible = false;
                    buttonRightPanel.AddChild(letterOptionContainer);

                    Slider sizeScrollBar = InseretUiForSlider(letterOptionContainer, "Size:");
                    Slider heightScrollBar = InseretUiForSlider(letterOptionContainer, "Height:");
                    Slider rotationScrollBar = InseretUiForSlider(letterOptionContainer, "Rotation:");

                    expandLetterOptions.CheckedStateChanged += (sender, e) =>
                    {
                        letterOptionContainer.Visible = expandLetterOptions.Checked;
                    };
                }

                GuiWidget verticalSpacer = new GuiWidget();
                verticalSpacer.VAnchor = VAnchor.ParentBottomTop;
                buttonRightPanel.AddChild(verticalSpacer);

                saveButton = whiteButtonFactory.Generate("Save", centerText: true);
                saveButton.Visible = false;
                saveButton.Cursor = Cursors.Hand;

                saveAndExitButton =  whiteButtonFactory.Generate("Save & Exit", centerText: true);
                saveAndExitButton.Visible = false;
                saveAndExitButton.Cursor = Cursors.Hand;

                //buttonRightPanel.AddChild(saveButton);
                buttonRightPanel.AddChild(saveAndExitButton);
            }

            buttonRightPanel.Padding = new BorderDouble(6, 6);
            buttonRightPanel.Margin = new BorderDouble(0, 1);
            buttonRightPanel.BackgroundColor = ActiveTheme.Instance.PrimaryBackgroundColor;
            buttonRightPanel.VAnchor = VAnchor.ParentBottomTop;

            return buttonRightPanel;
        }

        private void RebuildUnderlineIfRequired()
        {
            if (createUnderline.Checked)
            {
                // we need to remove the underline
                if (Meshes.Count > 1)
                {
                    int oldIndex = SelectedMeshIndex;
                    SelectedMeshIndex = Meshes.Count - 1;
                    DeleteSelectedMesh();
                    // we need to add the underline
                    CreateUnderline(Meshes, MeshTransforms, MeshPlatingData);
                    SelectedMeshIndex = oldIndex;
                }
            }
        }

        private void SetWordSpacing(List<Mesh> meshesList, List<ScaleRotateTranslate> meshTransforms, List<PlatingMeshData> platingDataList)
        {
            if (meshesList.Count > 0)
            {
                for (int meshIndex = 0; meshIndex < meshesList.Count; meshIndex++)
                {
                    Vector3 originPosition = Vector3.Transform(Vector3.Zero, meshTransforms[meshIndex].translation);

                    ScaleRotateTranslate translation = meshTransforms[meshIndex];
                    translation.translation *= Matrix4X4.CreateTranslation(new Vector3(-originPosition.x, 0, 0));
                    double newX = platingDataList[meshIndex].xSpacing * spacingScrollBar.Value * lastSizeValue;
                    translation.translation *= Matrix4X4.CreateTranslation(new Vector3(newX, 0, 0));
                    meshTransforms[meshIndex] = translation;
                }
            }
        }

        private void SetWordSize(List<Mesh> meshesList, List<ScaleRotateTranslate> meshTransforms)
        {
            if (meshesList.Count > 0)
            {
                for (int meshIndex = 0; meshIndex < meshesList.Count; meshIndex++)
                {
                    // take out the last scale
                    double oldSize = 1.0/lastSizeValue;
                    ScaleRotateTranslate scale = meshTransforms[meshIndex];
                    scale.scale *= Matrix4X4.CreateScale(new Vector3(oldSize, oldSize, oldSize));

                    double newSize = sizeScrollBar.Value;
                    scale.scale *= Matrix4X4.CreateScale(new Vector3(newSize, newSize, newSize));
                    meshTransforms[meshIndex] = scale;
                }

                lastSizeValue = sizeScrollBar.Value;
            }
        }

        private void SetWordHeight(List<Mesh> meshesList, List<ScaleRotateTranslate> meshTransforms)
        {
            if (meshesList.Count > 0)
            {
                for (int meshIndex = 0; meshIndex < meshesList.Count; meshIndex++)
                {
                    // take out the last scale
                    double oldHeight = lastHeightValue;
                    ScaleRotateTranslate scale = meshTransforms[meshIndex];
                    scale.scale *= Matrix4X4.CreateScale(new Vector3(1, 1, 1 / oldHeight));

                    double newHeight = heightScrollBar.Value;
                    scale.scale *= Matrix4X4.CreateScale(new Vector3(1, 1, newHeight));
                    meshTransforms[meshIndex] = scale;
                }

                lastHeightValue = heightScrollBar.Value;
            }
        }

        private void AddLetterControls(FlowLayoutWidget buttonPanel)
        {
            textImageButtonFactory.FixedWidth = 44;

            FlowLayoutWidget degreesContainer = new FlowLayoutWidget(FlowDirection.LeftToRight);
            degreesContainer.HAnchor = HAnchor.ParentLeftRight;
            degreesContainer.Padding = new BorderDouble(5);

            GuiWidget horizontalSpacer = new GuiWidget();
            horizontalSpacer.HAnchor = HAnchor.ParentLeftRight;

            TextWidget degreesLabel = new TextWidget("Degrees:", textColor: ActiveTheme.Instance.PrimaryTextColor);
            degreesContainer.AddChild(degreesLabel);
            degreesContainer.AddChild(horizontalSpacer);

            MHNumberEdit degreesControl = new MHNumberEdit(45, pixelWidth: 40, allowNegatives: true, increment: 5, minValue: -360, maxValue: 360);
            degreesControl.VAnchor = Agg.UI.VAnchor.ParentTop;
            degreesContainer.AddChild(degreesControl);

            buttonPanel.AddChild(degreesContainer);

            FlowLayoutWidget rotateButtonContainer = new FlowLayoutWidget(FlowDirection.LeftToRight);
            rotateButtonContainer.HAnchor = HAnchor.ParentLeftRight;

            buttonPanel.AddChild(rotateButtonContainer);

            buttonPanel.AddChild(generateHorizontalRule());
            textImageButtonFactory.FixedWidth = 0;
        }

        private GuiWidget generateHorizontalRule()
        {
            GuiWidget horizontalRule = new GuiWidget();
            horizontalRule.Height = 1;
            horizontalRule.Margin = new BorderDouble(0, 1, 0, 3);
            horizontalRule.HAnchor = HAnchor.ParentLeftRight;
            horizontalRule.BackgroundColor = new RGBA_Bytes(255, 255, 255, 200);
            return horizontalRule;
        }

        private void AddHandlers()
        {
            closeButton.Click += new ButtonBase.ButtonEventHandler(onCloseButton_Click);
            
            saveButton.Click += (sender, e) =>
            {
                MergeAndSavePartsToStl();
            };

            saveAndExitButton.Click += (sender, e) =>
            {
                MergeAndSavePartsToStl();
            };
        }

        bool partSelectButtonWasClicked = false;
        private void MergeAndSavePartsToStl()
        {
            if (Meshes.Count > 0)
            {
                partSelectButtonWasClicked = viewControls3D.partSelectButton.Checked;

                processingProgressControl.textWidget.Text = "Saving Parts:";
                processingProgressControl.Visible = true;
                processingProgressControl.PercentComplete = 0;
                LockEditControls();

                // we sent the data to the asynch lists but we will not pull it back out (only use it as a temp holder).
                PushMeshDataToAsynchLists(true);

                BackgroundWorker mergeAndSavePartsBackgroundWorker = new BackgroundWorker();
                mergeAndSavePartsBackgroundWorker.WorkerReportsProgress = true;

                mergeAndSavePartsBackgroundWorker.DoWork += new DoWorkEventHandler(mergeAndSavePartsBackgroundWorker_DoWork);
                mergeAndSavePartsBackgroundWorker.ProgressChanged += new ProgressChangedEventHandler(BackgroundWorker_ProgressChanged);
                mergeAndSavePartsBackgroundWorker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(mergeAndSavePartsBackgroundWorker_RunWorkerCompleted);

                mergeAndSavePartsBackgroundWorker.RunWorkerAsync();
            }
        }

        void mergeAndSavePartsBackgroundWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            BackgroundWorker backgroundWorker = (BackgroundWorker)sender;
            try
            {
                // push all the transforms into the meshes
                for (int i = 0; i < asynchMeshesList.Count; i++)
                {
                    asynchMeshesList[i].Transform(MeshTransforms[i].TotalTransform);

                    int nextPercent = (i + 1) * 40 / asynchMeshesList.Count;
                    backgroundWorker.ReportProgress(nextPercent);
                }

                Mesh mergedMesh = PlatingHelper.DoMerge(asynchMeshesList, backgroundWorker, 40, 80, true);

                string fileName = string.Format("UserCreated_{0}.stl", Path.GetRandomFileName());
                string filePath = Path.Combine(ApplicationDataStorage.Instance.ApplicationLibraryDataPath, fileName);
                StlProcessing.Save(mergedMesh, filePath);

                e.Result = filePath;
            }
            catch (System.UnauthorizedAccessException)
            {
                //Do something special when unauthorized?
                StyledMessageBox.ShowMessageBox("Oops! Unable to save changes.", "Unable to save");
            }
            catch
            {
                StyledMessageBox.ShowMessageBox("Oops! Unable to save changes.", "Unable to save");
            }
        }

        void mergeAndSavePartsBackgroundWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            string filePath = e.Result as string;
            if (filePath != null)
            {
                PrintItem printItem = new PrintItem();
                printItem.Commit();

                printItem.Name = string.Format("{0}", word);
                printItem.FileLocation = Path.GetFullPath(filePath);
                printItem.PrintItemCollectionID = LibraryData.Instance.LibraryCollection.Id;
                printItem.Commit();

                PrintItemWrapper printItemWrapper = new PrintItemWrapper(printItem);

                LibraryData.Instance.AddItem(printItemWrapper);

                // and save to the queue
                {
                    QueueData.Instance.AddItem(printItemWrapper);
                }
            }

            //Exit after save
            Close();
        }

        bool scaleQueueMenu_Click()
        {
            return true;
        }

        bool rotateQueueMenu_Click()
        {
            return true;
        }

        private void onCloseButton_Click(object sender, EventArgs e)
        {
            UiThread.RunOnIdle(CloseOnIdle);
        }

        void CloseOnIdle(object state)
        {
            Close();
        }
    }
}
