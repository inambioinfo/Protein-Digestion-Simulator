﻿Option Strict On

Imports System.IO
Imports SharedVBNetRoutines
Imports ValidateFastaFile

Public Class frmFastaValidation

    Public Sub New(strFastaFilePath As String)
        MyBase.New()

        'This call is required by the Windows Form Designer.
        InitializeComponent()

        'Add any initialization after the InitializeComponent() call
        InitializeForm(strFastaFilePath)
    End Sub


#Region "Constants and Enums"

    Private Const COL_NAME_LINE As String = "Line"
    Private Const COL_NAME_COLUMN As String = "Column"
    Private Const COL_NAME_PROTEIN As String = "Protein"
    Private Const COL_NAME_DESCRIPTION As String = "Description"
    Private Const COL_NAME_CONTEXT As String = "Context"

#End Region

#Region "Structures"
    Public Structure udtFastaValidationOptionsType
        Public Initialized As Boolean

        Public MaximumErrorsToTrackInDetail As Integer
        Public MaximumResiduesPerLine As Integer

        Public ValidProteinNameLengthMinimum As Integer
        Public ValidProteinNameLengthMaximum As Integer
        Public AllowAstericksInResidues As Boolean
        Public CheckForDuplicateProteinNames As Boolean
        Public LogResultsToFile As Boolean
        Public SaveHashInfoFile As Boolean

        Public FixedFastaOptions As udtFixedFastaOptionsType
    End Structure

    Public Structure udtFixedFastaOptionsType
        Public GenerateFixedFasta As Boolean
        Public TruncateLongProteinName As Boolean
        Public RenameProteinWithDuplicateNames As Boolean
        Public KeepDuplicateNamedProteins As Boolean
        Public WrapLongResidueLines As Boolean
        Public ResiduesPerLineForWrap As Integer
        Public RemoveInvalidResidues As Boolean
        Public SplitOutMultipleRefsForKnownAccession As Boolean
        Public SplitOutMultipleRefsInProteinName As Boolean
        Public ConsolidateDuplicateProteins As Boolean
        Public ConsolidateDupsIgnoreILDiff As Boolean
    End Structure
#End Region

#Region "Classwide variables"
    Private mFastaFilePath As String

    Private mErrorsDataset As DataSet
    Private mErrorsDataView As DataView

    Private mWarningsDataset As DataSet
    Private mWarningsDataView As DataView

    Private mKeepDuplicateNamedProteinsLastValue As Boolean = False

    ' This timer is used to cause StartValidation to be called after the form becomes visible
    Private WithEvents mValidationTriggerTimer As Timer
    Private WithEvents mValidateFastaFile As clsValidateFastaFile

    Private mValidatorErrorMessage As String

    Public Event FastaValidationStarted()
#End Region

#Region "Processing Options Interface Functions"
    Public Property CustomRulesFilePath() As String
        Get
            Return txtCustomValidationRulesFilePath.Text
        End Get
        Set(Value As String)
            txtCustomValidationRulesFilePath.Text = Value
        End Set
    End Property

    Public Property FastaFilePath() As String
        Get
            Return mFastaFilePath
        End Get
        Set(value As String)
            mFastaFilePath = value
        End Set
    End Property

    Public Property TextFontSize() As Single
        Get
            Return txtResults.Font.SizeInPoints
        End Get
        Set(Value As Single)
            If Value < 6 Then
                Value = 6
            ElseIf Value > 72 Then
                Value = 72
            End If

            Try
                txtResults.Font = New Font(txtResults.Font.FontFamily, Value)

                dgErrors.Font = New Font(txtResults.Font.FontFamily, Value)
                dgWarnings.Font = New Font(txtResults.Font.FontFamily, Value)

            Catch ex As Exception
                ' Ignore errors here
            End Try

        End Set
    End Property
#End Region

#Region "Procedures"

    Private Sub AppendToString(ByRef strText As String, strNewText As String)
        strText &= strNewText & ControlChars.NewLine
    End Sub

    Private Sub AppendToString(ByRef strText As String, strNumberDescription As String, lngNumber As Long)
        AppendToString(strText, strNumberDescription, lngNumber, True)
    End Sub

    Private Sub AppendToString(ByRef strText As String, strNumberDescription As String, lngNumber As Long, blnUseCommaSeparator As Boolean)

        ' Dim nfi As NumberFormatInfo = New CultureInfo("en-US", False).NumberFormat
        If blnUseCommaSeparator Then
            strText &= strNumberDescription & lngNumber.ToString("###,###,###,###,##0") & ControlChars.NewLine
        Else
            strText &= strNumberDescription & lngNumber.ToString & ControlChars.NewLine
        End If

    End Sub

    Private Sub CopyAllResults()
        Clipboard.SetDataObject(txtResults.Text & ControlChars.NewLine & FlattenDataView(mErrorsDataView) & ControlChars.NewLine & FlattenDataView(mWarningsDataView), True)
    End Sub

    Private Sub CopyErrorsDataView()
        Clipboard.SetDataObject(FlattenDataView(mErrorsDataView), True)
    End Sub

    Private Sub CopyWarningsDataView()
        Clipboard.SetDataObject(FlattenDataView(mWarningsDataView), True)
    End Sub

    Private Sub CreateDefaultValidationRulesFile()

        Dim objSaveFile As New SaveFileDialog()
        Dim strCustomRuleDefsFilePath As String

        With objSaveFile
            .AddExtension = True
            .CheckFileExists = False
            .CheckPathExists = True
            .CreatePrompt = False
            .DefaultExt = ".xml"
            .DereferenceLinks = True
            .OverwritePrompt = False
            .ValidateNames = True

            .Filter = "XML Settings Files (*.xml)|*.xml|All files (*.*)|*.*"
            .FilterIndex = 1

            If Len(txtCustomValidationRulesFilePath.Text.Length) > 0 Then
                Try
                    .InitialDirectory = Directory.GetParent(txtCustomValidationRulesFilePath.Text).ToString
                Catch
                    .InitialDirectory = GetApplicationDataFolderPath()
                End Try
            Else
                .InitialDirectory = GetApplicationDataFolderPath()
            End If

            .Title = "Select/Create file to save custom rule definitions"

            .ShowDialog()
            If .FileName.Length > 0 Then
                strCustomRuleDefsFilePath = .FileName

                Try
                    Dim objValidateFastaFile As New clsValidateFastaFile With {
                        .ShowMessages = True
                    }
                    objValidateFastaFile.SaveSettingsToParameterFile(strCustomRuleDefsFilePath)

                    If txtCustomValidationRulesFilePath.TextLength = 0 Then
                        txtCustomValidationRulesFilePath.Text = strCustomRuleDefsFilePath
                    End If

                    MessageBox.Show("File " & strCustomRuleDefsFilePath & " now contains the default rule validation settings.", "Done", MessageBoxButtons.OK, MessageBoxIcon.Information)

                Catch ex As Exception
                    MessageBox.Show("Error creating/updating file: " & strCustomRuleDefsFilePath & ControlChars.NewLine & ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Exclamation)
                End Try

            End If
        End With

    End Sub

    Private Sub DisplayResults(strParameterFilePath As String)
        ' Display the results

        Const strSepChar = ControlChars.Tab

        With mValidateFastaFile
            Dim strResults = "Results for file " & .FastaFilePath & ControlChars.NewLine

            AppendToString(strResults, "Protein count = " & .ProteinCount & strSepChar & strSepChar & "Residue count = ", .ResidueCount)
            AppendToString(strResults, "Error count = " & .ErrorWarningCounts(IValidateFastaFile.eMsgTypeConstants.ErrorMsg, IValidateFastaFile.ErrorWarningCountTypes.Total))
            AppendToString(strResults, "Warning count = ", .ErrorWarningCounts(IValidateFastaFile.eMsgTypeConstants.WarningMsg, IValidateFastaFile.ErrorWarningCountTypes.Total))

            If .OptionSwitch(IValidateFastaFile.SwitchOptions.GenerateFixedFASTAFile) Then
                AppendToString(strResults, "Count of long protein names that were truncated = " & .FixedFASTAFileStats(IValidateFastaFile.FixedFASTAFileValues.TruncatedProteinNameCount))
                AppendToString(strResults, "Count of protein names with invalid chars removed = " & .FixedFASTAFileStats(IValidateFastaFile.FixedFASTAFileValues.ProteinNamesInvalidCharsReplaced))
                AppendToString(strResults, "Count of protein names with multiple refs split out = " & .FixedFASTAFileStats(IValidateFastaFile.FixedFASTAFileValues.ProteinNamesMultipleRefsRemoved))
                AppendToString(strResults, "Count of residue lines with invalid chars removed = " & .FixedFASTAFileStats(IValidateFastaFile.FixedFASTAFileValues.UpdatedResidueLines))

                If .OptionSwitch(IValidateFastaFile.SwitchOptions.FixedFastaRenameDuplicateNameProteins) Then
                    AppendToString(strResults, "Count of proteins renamed due to duplicate names = " & .FixedFASTAFileStats(IValidateFastaFile.FixedFASTAFileValues.DuplicateProteinNamesRenamedCount))
                ElseIf .OptionSwitch(IValidateFastaFile.SwitchOptions.CheckForDuplicateProteinNames) Then
                    AppendToString(strResults, "Count of proteins skipped due to duplicate names = " & .FixedFASTAFileStats(IValidateFastaFile.FixedFASTAFileValues.DuplicateProteinNamesSkippedCount))
                End If

                If .OptionSwitch(IValidateFastaFile.SwitchOptions.FixedFastaConsolidateDuplicateProteinSeqs) Then
                    AppendToString(strResults, "Count of proteins removed due to duplicate sequences = " & .FixedFASTAFileStats(IValidateFastaFile.FixedFASTAFileValues.DuplicateProteinSeqsSkippedCount))
                End If

            End If

            If strParameterFilePath.Length > 0 Then
                AppendToString(strResults, "Used validation rules from file " & strParameterFilePath)
            Else
                AppendToString(strResults, "Default validation rules were used.")
            End If

            If .OptionSwitch(IValidateFastaFile.SwitchOptions.OutputToStatsFile) Then
                AppendToString(strResults, "Results were logged to file: " & .StatsFilePath())
            End If

            txtResults.Text = strResults
            txtResults.SelectionStart = 0
            txtResults.SelectionLength = 0

            ' Clear the filters
            txtFilterData.Text = String.Empty

            If .ErrorWarningCounts(IValidateFastaFile.eMsgTypeConstants.ErrorMsg, IValidateFastaFile.ErrorWarningCountTypes.Specified) > 0 Then
                ' List all of the errors
                PopulateMsgResultsDatagrid(dgErrors, mErrorsDataset, .FileErrorList)
            Else
                mErrorsDataset.Tables(0).Clear()
            End If

            If .ErrorWarningCounts(IValidateFastaFile.eMsgTypeConstants.WarningMsg, IValidateFastaFile.ErrorWarningCountTypes.Specified) > 0 Then
                ' List all of the warnings in the datagrid
                PopulateMsgResultsDatagrid(dgWarnings, mWarningsDataset, .FileWarningList)
            Else
                mWarningsDataset.Tables(0).Clear()
            End If

        End With

        PositionControls()
        FilterLists()
    End Sub

    Private Sub EnableDisableControls()
        Dim blnEnableFixedFastaOptions As Boolean

        blnEnableFixedFastaOptions = chkGenerateFixedFastaFile.Checked

        txtLongProteinNameSplitChars.Enabled = blnEnableFixedFastaOptions
        txtInvalidProteinNameCharsToRemove.Enabled = blnEnableFixedFastaOptions
        txtResiduesPerLineForWrap.Enabled = blnEnableFixedFastaOptions And chkWrapLongResidueLines.Checked

        chkSplitOutMultipleRefsInProteinName.Enabled = blnEnableFixedFastaOptions
        chkRenameDuplicateProteins.Enabled = blnEnableFixedFastaOptions

        If blnEnableFixedFastaOptions Then
            If chkRenameDuplicateProteins.Checked Then
                chkKeepDuplicateNamedProteins.Enabled = False
                chkKeepDuplicateNamedProteins.Checked = False
            Else
                chkKeepDuplicateNamedProteins.Enabled = True
                chkKeepDuplicateNamedProteins.Checked = mKeepDuplicateNamedProteinsLastValue
            End If
        Else
            chkKeepDuplicateNamedProteins.Enabled = False
        End If


        chkConsolidateDuplicateProteinSeqs.Enabled = blnEnableFixedFastaOptions
        chkConsolidateDupsIgnoreILDiff.Enabled = blnEnableFixedFastaOptions And chkConsolidateDuplicateProteinSeqs.Checked

        chkTruncateLongProteinNames.Enabled = blnEnableFixedFastaOptions
        chkSplitOutMultipleRefsForKnownAccession.Enabled = blnEnableFixedFastaOptions
        chkWrapLongResidueLines.Enabled = blnEnableFixedFastaOptions
        chkRemoveInvalidResidues.Enabled = blnEnableFixedFastaOptions

        If txtCustomValidationRulesFilePath.TextLength > 0 Then
            chkAllowAsteriskInResidues.Enabled = False
        Else
            chkAllowAsteriskInResidues.Enabled = True
        End If

    End Sub

    Private Function FlattenDataView(dvDataView As DataView) As String
        ' Copy the contents of the datagrid to the clipboard

        Const chSepChar As Char = ControlChars.Tab

        Dim strText As String = String.Empty
        Dim intIndex As Integer
        Dim intColumnCount As Integer

        'Dim txtResults As New Windows.Forms.TextBox

        Dim objRow As DataRow

        Try
            intColumnCount = dvDataView.Table.Columns.Count
            With dvDataView.Table.Columns
                For intIndex = 0 To intColumnCount - 1
                    If intIndex < intColumnCount - 1 Then
                        strText &= .Item(intIndex).ColumnName.ToString & chSepChar
                    Else
                        strText &= .Item(intIndex).ColumnName.ToString & ControlChars.NewLine
                    End If
                Next intIndex
            End With

            For Each objRow In dvDataView.Table.Rows
                For intIndex = 0 To intColumnCount - 1
                    If intIndex < intColumnCount - 1 Then
                        strText &= objRow(intIndex).ToString & chSepChar
                    Else
                        strText &= objRow(intIndex).ToString & ControlChars.NewLine
                    End If
                Next intIndex
            Next objRow

        Catch ex As Exception
            strText &= ControlChars.NewLine & ControlChars.NewLine & "Error copying data: " & ex.Message
        End Try

        Return strText

    End Function

    Private Sub CopySummaryText()
        Dim intSelStart As Integer
        Dim intSelLength As Integer

        With txtResults
            intSelStart = .SelectionStart
            intSelLength = .SelectionLength

            .SelectAll()
            .Copy()

            .SelectionStart = intSelStart
            .SelectionLength = intSelLength
        End With

    End Sub

    Private Sub FilterLists()

        Dim strFilter As String

        Try
            strFilter = String.Empty
            If txtFilterData.TextLength > 0 Then
                strFilter = "[" & COL_NAME_PROTEIN & "] LIKE '%" & txtFilterData.Text & "%' OR [" & COL_NAME_DESCRIPTION & "] LIKE '%" & txtFilterData.Text & "%' OR [" & COL_NAME_CONTEXT & "] LIKE '%" & txtFilterData.Text & "%'"
            End If

            mErrorsDataView.RowFilter = strFilter
            dgErrors.Update()

            mWarningsDataView.RowFilter = strFilter
            dgWarnings.Update()

        Catch ex As Exception
            MessageBox.Show("Error filtering lists: " & ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Exclamation)
        End Try

    End Sub

    Private Function GetApplicationDataFolderPath() As String
        Dim strAppDataFolderPath As String = String.Empty

        Try
            strAppDataFolderPath = Path.Combine(
               Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
               "PAST Toolkit\ProteinDigestionSimulator")

            If Not Directory.Exists(strAppDataFolderPath) Then
                Directory.CreateDirectory(strAppDataFolderPath)
            End If

        Catch ex As Exception
            ' Ignore errors here; an exception will likely be thrown by the calling function that is trying to access this non-existent application data folder
        End Try

        Return strAppDataFolderPath

    End Function

    Public Function GetOptions() As udtFastaValidationOptionsType

        Dim udtFastaValidationOptions As udtFastaValidationOptionsType

        With udtFastaValidationOptions
            .MaximumErrorsToTrackInDetail = VBNetRoutines.ParseTextboxValueInt(txtMaxFileErrorsToTrack, "", False, 10, False)
            .MaximumResiduesPerLine = VBNetRoutines.ParseTextboxValueInt(txtMaximumResiduesPerLine, "", False, 120, False)
            .ValidProteinNameLengthMinimum = VBNetRoutines.ParseTextboxValueInt(txtProteinNameLengthMinimum, "", False, 3, False)
            .ValidProteinNameLengthMaximum = VBNetRoutines.ParseTextboxValueInt(txtProteinNameLengthMaximum, "", False, 34, False)

            .AllowAstericksInResidues = chkAllowAsteriskInResidues.Checked
            .CheckForDuplicateProteinNames = chkCheckForDuplicateProteinInfo.Checked
            .LogResultsToFile = chkLogResults.Checked
            .SaveHashInfoFile = chkSaveBasicProteinHashInfoFile.Checked

            With .FixedFastaOptions
                .GenerateFixedFasta = chkGenerateFixedFastaFile.Checked
                .TruncateLongProteinName = chkTruncateLongProteinNames.Checked
                .RenameProteinWithDuplicateNames = chkRenameDuplicateProteins.Checked
                .KeepDuplicateNamedProteins = chkKeepDuplicateNamedProteins.Checked
                .WrapLongResidueLines = chkWrapLongResidueLines.Checked
                .RemoveInvalidResidues = chkRemoveInvalidResidues.Checked
                .SplitOutMultipleRefsForKnownAccession = chkSplitOutMultipleRefsForKnownAccession.Checked
                .SplitOutMultipleRefsInProteinName = chkSplitOutMultipleRefsInProteinName.Checked
                .ConsolidateDuplicateProteins = chkConsolidateDuplicateProteinSeqs.Checked
                .ConsolidateDupsIgnoreILDiff = chkConsolidateDupsIgnoreILDiff.Checked
                .ResiduesPerLineForWrap = VBNetRoutines.ParseTextboxValueInt(txtResiduesPerLineForWrap, "", False, 60, False)
            End With

            .Initialized = True
        End With

        Return udtFastaValidationOptions

    End Function

    Private Sub InitializeDataGrid(dgDataGrid As DataGrid, ByRef dsDataset As DataSet, ByRef dvDataView As DataView, eMsgType As IValidateFastaFile.eMsgTypeConstants)

        Dim dtDataTable As DataTable

        Dim strMsgColumnName As String
        Dim strDatasetName As String
        Dim strDatatableName As String

        If eMsgType = IValidateFastaFile.eMsgTypeConstants.WarningMsg Then
            strMsgColumnName = "Warnings"
        ElseIf eMsgType = IValidateFastaFile.eMsgTypeConstants.ErrorMsg Then
            strMsgColumnName = "Errors"
        Else
            strMsgColumnName = "Status"
        End If

        strDatasetName = "ds" & strMsgColumnName
        strDatatableName = "T_" & strMsgColumnName

        ' Create a DataTable
        dtDataTable = New DataTable(strDatatableName)

        ' Add the columns to the datatable
        ADONetRoutines.AppendColumnIntegerToTable(dtDataTable, COL_NAME_LINE)
        ADONetRoutines.AppendColumnIntegerToTable(dtDataTable, COL_NAME_COLUMN)
        ADONetRoutines.AppendColumnStringToTable(dtDataTable, COL_NAME_PROTEIN)
        ADONetRoutines.AppendColumnStringToTable(dtDataTable, COL_NAME_DESCRIPTION)
        ADONetRoutines.AppendColumnStringToTable(dtDataTable, COL_NAME_CONTEXT)

        ' Could define a primary key
        ''With dtDataTable
        ''    Dim PrimaryKeyColumn As DataColumn() = New DataColumn() {.Columns(COL_NAME_LINE)}
        ''    .PrimaryKey = PrimaryKeyColumn
        ''End With

        ' Instantiate the DataSet
        dsDataset = New DataSet(strDatasetName)

        ' Add the table to the DataSet
        dsDataset.Tables.Add(dtDataTable)

        ' Instantiate the DataView
        dvDataView = New DataView
        With dvDataView
            .Table = dsDataset.Tables(strDatatableName)
            .RowFilter = String.Empty
        End With

        ' Bind the DataSet to the DataGrid
        With dgDataGrid
            .DataSource = dvDataView
            .ReadOnly = False
            .CaptionText = strMsgColumnName
        End With

        ' Update the grid's table style
        UpdateDatagridTableStyle(dgDataGrid, strDatatableName)

    End Sub

    Private Sub InitializeForm(strFastaFilePath As String)
        txtResults.ReadOnly = True
        Me.TextFontSize = 10

        InitializeDataGrid(dgErrors, mErrorsDataset, mErrorsDataView, IValidateFastaFile.eMsgTypeConstants.ErrorMsg)
        InitializeDataGrid(dgWarnings, mWarningsDataset, mWarningsDataView, IValidateFastaFile.eMsgTypeConstants.WarningMsg)

        mValidationTriggerTimer = New Timer()
        With mValidationTriggerTimer
            .Interval = 100
            .Enabled = True
        End With

        SetNewFastaFile(strFastaFilePath)

        EnableDisableControls()
        SetToolTips()

        ResetOptionsToDefault()

    End Sub

    Private Sub PopulateMsgResultsDatagrid(dgDataGrid As Control, dsDataset As DataSet, itemList As IEnumerable(Of IValidateFastaFile.udtMsgInfoType))

        ' Clear the table
        dsDataset.Tables(0).Clear()

        ' Populate it with new data
        For Each item In itemList
            Dim objRow = dsDataset.Tables(0).NewRow()
            With item
                objRow(COL_NAME_LINE) = .LineNumber
                objRow(COL_NAME_COLUMN) = .ColNumber
                If .ProteinName Is Nothing OrElse .ProteinName.Length = 0 Then
                    objRow(COL_NAME_PROTEIN) = "N/A"
                Else
                    objRow(COL_NAME_PROTEIN) = .ProteinName
                End If

                objRow(COL_NAME_DESCRIPTION) = mValidateFastaFile.LookupMessageDescription(.MessageCode, .ExtraInfo)
                objRow(COL_NAME_CONTEXT) = .Context
            End With

            ' Add the row to the table
            dsDataset.Tables(0).Rows.Add(objRow)
        Next

        dgDataGrid.Update()

    End Sub

    Private Sub PositionControls()

        Const MAX_RATIO As Single = 1.5
        Const MENU_HEIGHT = 80

        Dim intDesiredHeight As Integer

        Dim dblErrorToWarningsRatio As Double
        dblErrorToWarningsRatio = 1

        Try
            If Not mErrorsDataset Is Nothing AndAlso Not mWarningsDataset Is Nothing Then
                If mErrorsDataset.Tables(0).Rows.Count = 0 And mWarningsDataset.Tables(0).Rows.Count = 0 Then
                    dblErrorToWarningsRatio = 1
                ElseIf mErrorsDataset.Tables(0).Rows.Count = 0 Then
                    dblErrorToWarningsRatio = 1 / MAX_RATIO
                ElseIf mWarningsDataset.Tables(0).Rows.Count = 0 Then
                    dblErrorToWarningsRatio = MAX_RATIO
                Else
                    dblErrorToWarningsRatio = mErrorsDataset.Tables(0).Rows.Count / mWarningsDataset.Tables(0).Rows.Count

                    If dblErrorToWarningsRatio < 1 / MAX_RATIO Then
                        dblErrorToWarningsRatio = 1 / MAX_RATIO
                    ElseIf dblErrorToWarningsRatio > MAX_RATIO Then
                        dblErrorToWarningsRatio = MAX_RATIO
                    End If
                End If
            End If
        Catch ex As Exception
            dblErrorToWarningsRatio = 1
        End Try

        If dblErrorToWarningsRatio >= 1 Then
            ' Errors grid should be taller
            intDesiredHeight = CInt(Math.Round((Me.Height - dgErrors.Top - MENU_HEIGHT) / (dblErrorToWarningsRatio + 1) * dblErrorToWarningsRatio, 0))
        Else
            ' Errors grid should be shorter
            intDesiredHeight = CInt(Math.Round((Me.Height - dgErrors.Top - MENU_HEIGHT) / (1 / dblErrorToWarningsRatio + 1), 0)) - 2
        End If

        If intDesiredHeight < 5 Then intDesiredHeight = 5
        dgErrors.Height = intDesiredHeight

        dgWarnings.Top = dgErrors.Top + dgErrors.Height + 10

        intDesiredHeight = CInt(Math.Round(intDesiredHeight / dblErrorToWarningsRatio, 0))
        If intDesiredHeight < 5 Then intDesiredHeight = 5
        dgWarnings.Height = intDesiredHeight

    End Sub

    Private Sub ResetOptionsToDefault()
        txtMaxFileErrorsToTrack.Text = "10"

        txtMaximumResiduesPerLine.Text = "120"

        txtProteinNameLengthMinimum.Text = "3"
        txtProteinNameLengthMaximum.Text = "34"

        txtLongProteinNameSplitChars.Text = clsValidateFastaFile.DEFAULT_LONG_PROTEIN_NAME_SPLIT_CHAR
        txtInvalidProteinNameCharsToRemove.Text = ""
        txtResiduesPerLineForWrap.Text = "60"

        chkAllowAsteriskInResidues.Checked = False
        chkCheckForDuplicateProteinInfo.Checked = True
        chkSaveBasicProteinHashInfoFile.Checked = False

        chkLogResults.Checked = False

        ' Note: Leave this option unchanged
        'chkGenerateFixedFastaFile.Checked = False

        chkConsolidateDuplicateProteinSeqs.Checked = True
        chkConsolidateDupsIgnoreILDiff.Checked = True

        chkRemoveInvalidResidues.Checked = False
        chkRenameDuplicateProteins.Checked = True
        chkKeepDuplicateNamedProteins.Checked = False
        chkSplitOutMultipleRefsForKnownAccession.Checked = False
        chkSplitOutMultipleRefsInProteinName.Checked = False
        chkTruncateLongProteinNames.Checked = True
        chkWrapLongResidueLines.Checked = True
    End Sub

    Private Sub SelectCustomRulesFile()

        Dim objOpenFile As New OpenFileDialog()

        With objOpenFile
            .AddExtension = True
            .CheckFileExists = True
            .CheckPathExists = True
            .DefaultExt = ".xml"
            .DereferenceLinks = True
            .Multiselect = False
            .ValidateNames = True

            .Filter = "XML Settings Files (*.xml)|*.xml|All files (*.*)|*.*"
            .FilterIndex = 1

            If Len(txtCustomValidationRulesFilePath.Text.Length) > 0 Then
                Try
                    .InitialDirectory = Directory.GetParent(txtCustomValidationRulesFilePath.Text).ToString
                Catch
                    .InitialDirectory = GetApplicationDataFolderPath()
                End Try
            Else
                .InitialDirectory = GetApplicationDataFolderPath()
            End If

            .Title = "Select custom rules file"

            .ShowDialog()
            If .FileName.Length > 0 Then
                txtCustomValidationRulesFilePath.Text = .FileName
            End If
        End With

    End Sub

    Public Sub SetNewFastaFile(strFastaFilePath As String)
        mFastaFilePath = strFastaFilePath

        txtResults.Text = String.Empty

        ' Clear the filters
        txtFilterData.Text = String.Empty

        mErrorsDataset.Tables(0).Clear()
        mWarningsDataset.Tables(0).Clear()

    End Sub

    Private Sub ShowAboutBox()
        Dim strMessage As String

        strMessage = String.Empty

        strMessage &= "Fasta File Validation module written by Matthew Monroe for the Department of Energy (PNNL, Richland, WA) in 2005" & ControlChars.NewLine
        strMessage &= "Copyright 2005, Battelle Memorial Institute.  All Rights Reserved." & ControlChars.NewLine & ControlChars.NewLine

        strMessage &= "This is version " & Application.ProductVersion & " (" & PROGRAM_DATE & ")" & ControlChars.NewLine & ControlChars.NewLine

        strMessage &= "E-mail: matthew.monroe@pnnl.gov or matt@alchemistmatt.com" & ControlChars.NewLine
        strMessage &= "Website: http://omics.pnl.gov/ or http://www.sysbio.org/resources/staff/ or http://panomics.pnnl.gov/" & ControlChars.NewLine & ControlChars.NewLine

        strMessage &= "Licensed under the Apache License, Version 2.0; you may not use this file except in compliance with the License.  "
        strMessage &= "You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0" & ControlChars.NewLine & ControlChars.NewLine

        strMessage &= "Notice: This computer software was prepared by Battelle Memorial Institute, "
        strMessage &= "hereinafter the Contractor, under Contract No. DE-AC05-76RL0 1830 with the "
        strMessage &= "Department of Energy (DOE).  All rights in the computer software are reserved "
        strMessage &= "by DOE on behalf of the United States Government and the Contractor as "
        strMessage &= "provided in the Contract.  NEITHER THE GOVERNMENT NOR THE CONTRACTOR MAKES ANY "
        strMessage &= "WARRANTY, EXPRESS OR IMPLIED, OR ASSUMES ANY LIABILITY FOR THE USE OF THIS "
        strMessage &= "SOFTWARE.  This notice including this sentence must appear on any copies of "
        strMessage &= "this computer software." & ControlChars.NewLine

        MessageBox.Show(strMessage, "About", MessageBoxButtons.OK, MessageBoxIcon.Information)

    End Sub

    Private Sub ShowHideObjectsDuringValidation(blnValidating As Boolean)
        pbarProgress.Visible = blnValidating

        lblFilterData.Visible = Not blnValidating
        txtFilterData.Visible = Not blnValidating

        cmdValidateFastaFile.Visible = Not blnValidating
        cmdValidateFastaFile.Enabled = Not blnValidating

        cmdCancel.Visible = blnValidating
        cmdCancel.Enabled = blnValidating

    End Sub

    Public Sub SetOptions(udtFastaValidationOptions As udtFastaValidationOptionsType)

        With udtFastaValidationOptions
            txtMaxFileErrorsToTrack.Text = .MaximumErrorsToTrackInDetail.ToString
            txtMaximumResiduesPerLine.Text = .MaximumResiduesPerLine.ToString()

            txtProteinNameLengthMinimum.Text = .ValidProteinNameLengthMinimum.ToString()
            txtProteinNameLengthMaximum.Text = .ValidProteinNameLengthMaximum.ToString()

            chkAllowAsteriskInResidues.Checked = .AllowAstericksInResidues
            chkCheckForDuplicateProteinInfo.Checked = .CheckForDuplicateProteinNames
            chkLogResults.Checked = .LogResultsToFile
            chkSaveBasicProteinHashInfoFile.Checked = .SaveHashInfoFile

            With .FixedFastaOptions
                chkGenerateFixedFastaFile.Checked = .GenerateFixedFasta
                chkTruncateLongProteinNames.Checked = .TruncateLongProteinName
                chkRenameDuplicateProteins.Checked = .RenameProteinWithDuplicateNames
                chkKeepDuplicateNamedProteins.Checked = .KeepDuplicateNamedProteins
                chkWrapLongResidueLines.Checked = .WrapLongResidueLines
                chkRemoveInvalidResidues.Checked = .RemoveInvalidResidues
                chkSplitOutMultipleRefsForKnownAccession.Checked = .SplitOutMultipleRefsForKnownAccession
                chkSplitOutMultipleRefsInProteinName.Checked = .SplitOutMultipleRefsInProteinName
                chkConsolidateDuplicateProteinSeqs.Checked = .ConsolidateDuplicateProteins
                chkConsolidateDupsIgnoreILDiff.Checked = .ConsolidateDupsIgnoreILDiff
                txtResiduesPerLineForWrap.Text = .ResiduesPerLineForWrap.ToString()
            End With
        End With

        Application.DoEvents()

    End Sub

    Private Sub SetToolTips()
        Dim objToolTipControl As New ToolTip()

        With objToolTipControl
            .SetToolTip(txtLongProteinNameSplitChars, "Enter one or more characters to look for when truncating long protein names (do not separate the characters by commas).  Default character is a vertical bar.")
            .SetToolTip(txtInvalidProteinNameCharsToRemove, "Enter one or more characters to look and replace with an underscore (do not separate the characters by commas).  Leave blank to not replace any characters.")
            .SetToolTip(chkSplitOutMultipleRefsForKnownAccession, "If a protein name matches the standard IPI, GI, or JGI accession numbers, and if it contains additional reference information, then the additional information will be moved to the protein's description.")
            .SetToolTip(chkSaveBasicProteinHashInfoFile, "To minimize memory usage, enable this option by disable 'Check for Duplicate Proteins'")
        End With

    End Sub

    Private Sub StartValidation()

        Dim strParameterFilePath As String
        Dim blnFileExists As Boolean

        Dim blnSuccess As Boolean

        Try
            If mValidateFastaFile Is Nothing Then
                mValidateFastaFile = New clsValidateFastaFile
            End If

            mValidatorErrorMessage = String.Empty

            RaiseEvent FastaValidationStarted()

            ShowHideObjectsDuringValidation(True)
            Cursor.Current = Cursors.WaitCursor
            Application.DoEvents()

            With mValidateFastaFile
                .ShowMessages = True

                ' Note: the following settings will be overridden if a parameter file with these settings defined is provided to .ProcessFile()
                .SetOptionSwitch(IValidateFastaFile.SwitchOptions.WarnBlankLinesBetweenProteins, True)
                .SetOptionSwitch(IValidateFastaFile.SwitchOptions.AllowAsteriskInResidues, chkAllowAsteriskInResidues.Checked)

                .MinimumProteinNameLength = VBNetRoutines.ParseTextboxValueInt(txtProteinNameLengthMinimum, "Minimum protein name length should be a number", False, 3, False)
                .MaximumProteinNameLength = VBNetRoutines.ParseTextboxValueInt(txtProteinNameLengthMaximum, "Maximum protein name length should be a number", False, 34, False)

                If chkGenerateFixedFastaFile.Checked AndAlso chkWrapLongResidueLines.Checked Then
                    .MaximumResiduesPerLine = VBNetRoutines.ParseTextboxValueInt(txtResiduesPerLineForWrap, "Residues per line for wrapping should be a number", False, 60, False)
                Else
                    .MaximumResiduesPerLine = VBNetRoutines.ParseTextboxValueInt(txtMaximumResiduesPerLine, "Maximum residues per line should be a number", False, 120, False)
                End If

                strParameterFilePath = txtCustomValidationRulesFilePath.Text
                If strParameterFilePath.Length > 0 Then
                    Try
                        blnFileExists = File.Exists(strParameterFilePath)
                    Catch ex As Exception
                        blnFileExists = False
                    End Try

                    If Not blnFileExists Then
                        MessageBox.Show("Custom rules validation file not found: " & ControlChars.NewLine & strParameterFilePath & ControlChars.NewLine & "Tthe default validation rules will be used instead.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Exclamation)
                        strParameterFilePath = String.Empty
                    Else
                        mValidateFastaFile.LoadParameterFileSettings(strParameterFilePath)
                    End If
                End If

                If strParameterFilePath.Length = 0 Then
                    .SetDefaultRules()
                End If

                .MaximumFileErrorsToTrack = VBNetRoutines.ParseTextboxValueInt(txtMaxFileErrorsToTrack, "Max file errors or warnings should be a positive number", False, 10, False)

                .SetOptionSwitch(IValidateFastaFile.SwitchOptions.CheckForDuplicateProteinNames, chkCheckForDuplicateProteinInfo.Checked)
                .SetOptionSwitch(IValidateFastaFile.SwitchOptions.CheckForDuplicateProteinSequences, chkCheckForDuplicateProteinInfo.Checked)
                .SetOptionSwitch(IValidateFastaFile.SwitchOptions.SaveBasicProteinHashInfoFile, chkSaveBasicProteinHashInfoFile.Checked)
                .SetOptionSwitch(IValidateFastaFile.SwitchOptions.OutputToStatsFile, chkLogResults.Checked)

                .SetOptionSwitch(IValidateFastaFile.SwitchOptions.GenerateFixedFASTAFile, chkGenerateFixedFastaFile.Checked)

                .SetOptionSwitch(IValidateFastaFile.SwitchOptions.FixedFastaSplitOutMultipleRefsForKnownAccession, chkSplitOutMultipleRefsForKnownAccession.Checked)
                .SetOptionSwitch(IValidateFastaFile.SwitchOptions.SplitOutMultipleRefsInProteinName, chkSplitOutMultipleRefsInProteinName.Checked)

                .SetOptionSwitch(IValidateFastaFile.SwitchOptions.FixedFastaRenameDuplicateNameProteins, chkRenameDuplicateProteins.Checked)
                .SetOptionSwitch(IValidateFastaFile.SwitchOptions.FixedFastaKeepDuplicateNamedProteins, chkKeepDuplicateNamedProteins.Checked)
                .SetOptionSwitch(IValidateFastaFile.SwitchOptions.FixedFastaConsolidateDuplicateProteinSeqs, chkConsolidateDuplicateProteinSeqs.Checked)
                .SetOptionSwitch(IValidateFastaFile.SwitchOptions.FixedFastaConsolidateDupsIgnoreILDiff, chkConsolidateDupsIgnoreILDiff.Checked)
                .SetOptionSwitch(IValidateFastaFile.SwitchOptions.FixedFastaTruncateLongProteinNames, chkTruncateLongProteinNames.Checked)
                .SetOptionSwitch(IValidateFastaFile.SwitchOptions.FixedFastaWrapLongResidueLines, chkWrapLongResidueLines.Checked)
                .SetOptionSwitch(IValidateFastaFile.SwitchOptions.FixedFastaRemoveInvalidResidues, chkRemoveInvalidResidues.Checked())

                .ProteinNameFirstRefSepChars = clsValidateFastaFile.DEFAULT_PROTEIN_NAME_FIRST_REF_SEP_CHARS
                .ProteinNameSubsequentRefSepChars = clsValidateFastaFile.DEFAULT_PROTEIN_NAME_SUBSEQUENT_REF_SEP_CHARS

                ' Also apply chkGenerateFixedFastaFile to SaveProteinSequenceHashInfoFiles
                .SetOptionSwitch(IValidateFastaFile.SwitchOptions.SaveProteinSequenceHashInfoFiles, chkGenerateFixedFastaFile.Checked)

                If txtLongProteinNameSplitChars.TextLength > 0 Then
                    .LongProteinNameSplitChars = txtLongProteinNameSplitChars.Text
                Else
                    .LongProteinNameSplitChars = clsValidateFastaFile.DEFAULT_LONG_PROTEIN_NAME_SPLIT_CHAR
                End If

                .ProteinNameInvalidCharsToRemove = txtInvalidProteinNameCharsToRemove.Text

            End With

            With pbarProgress
                .Minimum = 0
                .Maximum = 100
                .Value = 0
            End With


            ' Analyze the fasta file; returns true if the analysis was successful (even if the file contains errors or warnings)
            blnSuccess = mValidateFastaFile.ProcessFile(mFastaFilePath, String.Empty, String.Empty)

            cmdCancel.Enabled = False

            If blnSuccess Then
                DisplayResults(strParameterFilePath)
            Else
                txtResults.Text = "Error calling mValidateFastaFile.ProcessFile: " & mValidateFastaFile.GetErrorMessage()
                If Not String.IsNullOrEmpty(mValidatorErrorMessage) Then
                    txtResults.AppendText(ControlChars.NewLine & mValidatorErrorMessage)
                End If
            End If

        Catch ex As Exception
            MessageBox.Show("Error validating fasta file: " & mFastaFilePath & ControlChars.NewLine & ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Exclamation)
        Finally
            mValidateFastaFile = Nothing
            ShowHideObjectsDuringValidation(False)

            Cursor.Current = Cursors.Default
            Application.DoEvents()
        End Try

    End Sub

    Private Sub UpdateDatagridTableStyle(dgDataGrid As DataGrid, strTargetTableName As String)

        Dim tsTableStyle As DataGridTableStyle

        ' Instantiate the TableStyle
        tsTableStyle = New DataGridTableStyle

        ' Setting the MappingName of the table style to strTargetTableName will cause this style to be used with that table
        With tsTableStyle
            .MappingName = strTargetTableName
            .AllowSorting = True
            .ColumnHeadersVisible = True
            .RowHeadersVisible = False
            .ReadOnly = True
        End With

        ADONetRoutines.AppendColumnToTableStyle(tsTableStyle, COL_NAME_LINE, "Line", 80)
        ADONetRoutines.AppendColumnToTableStyle(tsTableStyle, COL_NAME_COLUMN, "Column", 80)
        ADONetRoutines.AppendColumnToTableStyle(tsTableStyle, COL_NAME_PROTEIN, "Protein", 200)
        ADONetRoutines.AppendColumnToTableStyle(tsTableStyle, COL_NAME_DESCRIPTION, "Value", 550)
        ADONetRoutines.AppendColumnToTableStyle(tsTableStyle, COL_NAME_CONTEXT, "Context", 200)

        With dgDataGrid
            .TableStyles.Clear()

            If Not .TableStyles.Contains(tsTableStyle) Then
                .TableStyles.Add(tsTableStyle)
            End If

            .Refresh()
        End With

    End Sub

#End Region

#Region "Control Handlers"

    Private Sub chkGenerateFixedFastaFile_CheckedChanged(sender As Object, e As EventArgs) Handles chkGenerateFixedFastaFile.CheckedChanged
        EnableDisableControls()
    End Sub

    Private Sub chkConsolidateDuplicateProteinSeqs_CheckedChanged(sender As Object, e As EventArgs) Handles chkConsolidateDuplicateProteinSeqs.CheckedChanged
        EnableDisableControls()
    End Sub

    Private Sub chkRenameDuplicateProteins_CheckedChanged(sender As Object, e As EventArgs) Handles chkRenameDuplicateProteins.CheckedChanged
        EnableDisableControls()
    End Sub

    Private Sub chkKeepDuplicateNamedProteins_CheckedChanged(sender As Object, e As EventArgs) Handles chkKeepDuplicateNamedProteins.CheckedChanged
        If chkKeepDuplicateNamedProteins.Enabled Then
            mKeepDuplicateNamedProteinsLastValue = chkKeepDuplicateNamedProteins.Checked
        End If
    End Sub

    Private Sub chkWrapLongResidueLines_CheckedChanged(sender As Object, e As EventArgs) Handles chkWrapLongResidueLines.CheckedChanged
        EnableDisableControls()
    End Sub

    Private Sub cmdCancel_Click(sender As Object, e As EventArgs) Handles cmdCancel.Click
        If Not mValidateFastaFile Is Nothing Then
            cmdCancel.Enabled = False
            mValidateFastaFile.AbortProcessingNow()
        End If
    End Sub

    Private Sub cmdCreateDefaultValidationRulesFile_Click_1(sender As Object, e As EventArgs) Handles cmdCreateDefaultValidationRulesFile.Click
        CreateDefaultValidationRulesFile()
    End Sub

    Private Sub cmdSelectCustomRulesFile_Click(sender As Object, e As EventArgs) Handles cmdSelectCustomRulesFile.Click
        SelectCustomRulesFile()
    End Sub

    Private Sub cmdValidateFastaFile_Click(sender As Object, e As EventArgs) Handles cmdValidateFastaFile.Click
        StartValidation()
    End Sub

    Private Sub frmFastaValidationResults_Resize(sender As Object, e As EventArgs) Handles MyBase.Resize
        PositionControls()
    End Sub

    Private Sub txtCustomValidationRulesFilePath_TextChanged(sender As Object, e As EventArgs) Handles txtCustomValidationRulesFilePath.TextChanged
        EnableDisableControls()
    End Sub

    Private Sub txtFilterData_Validated(sender As Object, e As EventArgs) Handles txtFilterData.Validated
        FilterLists()
    End Sub

    Private Sub txtFilterData_KeyDown(sender As Object, e As KeyEventArgs) Handles txtFilterData.KeyDown
        If e.KeyCode = Keys.Enter Then
            FilterLists()
        End If
    End Sub

    Private Sub txtMaxFileErrorsToTrack_KeyPress1(sender As Object, e As KeyPressEventArgs) Handles txtMaxFileErrorsToTrack.KeyPress
        VBNetRoutines.TextBoxKeyPressHandler(txtMaxFileErrorsToTrack, e, True)
    End Sub

    Private Sub txtMaximumResiduesPerLine_KeyPress(sender As Object, e As KeyPressEventArgs) Handles txtMaximumResiduesPerLine.KeyPress
        VBNetRoutines.TextBoxKeyPressHandler(txtMaximumResiduesPerLine, e, True)
    End Sub

    Private Sub txtProteinNameLengthMinimum_KeyPress(sender As Object, e As KeyPressEventArgs) Handles txtProteinNameLengthMinimum.KeyPress
        VBNetRoutines.TextBoxKeyPressHandler(txtProteinNameLengthMinimum, e, True)
    End Sub

    Private Sub txtProteinNameLengthMaximum_KeyPress(sender As Object, e As KeyPressEventArgs) Handles txtProteinNameLengthMaximum.KeyPress
        VBNetRoutines.TextBoxKeyPressHandler(txtProteinNameLengthMaximum, e, True)
    End Sub

    Private Sub txtResiduesPerLineForWrap_TextChanged(sender As Object, e As KeyPressEventArgs) Handles txtResiduesPerLineForWrap.KeyPress
        VBNetRoutines.TextBoxKeyPressHandler(txtResiduesPerLineForWrap, e, True)
    End Sub

    Private Sub txtResults_KeyDown(sender As Object, e As KeyEventArgs) Handles txtResults.KeyDown
        If e.Control = True Then
            If e.KeyCode = Keys.A Then
                txtResults.SelectAll()
            End If
        End If
    End Sub

    ''' <summary>
    ''' This timer is used to cause StartValidation to be called after the form becomes visible
    ''' </summary>
    ''' <param name="sender"></param>
    ''' <param name="e"></param>
    ''' <remarks></remarks>
    Private Sub mValidationTriggerTimer_Tick(sender As Object, e As EventArgs) Handles mValidationTriggerTimer.Tick
        mValidationTriggerTimer.Enabled = False

        Try
            ' Check whether the fasta file is over 250 MB in size
            ' If it is, auto-disable the check for duplicate proteins (to avoid using too much memory)
            Dim fiFastaFile = New FileInfo(mFastaFilePath)
            If fiFastaFile.Exists Then
                If fiFastaFile.Length > 250 * 1024 * 1024 Then
                    chkCheckForDuplicateProteinInfo.Checked = False
                End If
                StartValidation()
            End If

        Catch ex As Exception
            MessageBox.Show("Error examining the fasta file size: " & ex.Message)
        End Try

    End Sub

#End Region

#Region "Menu Handlers"

    Private Sub mnuFileExit_Click(sender As Object, e As EventArgs) Handles mnuFileExit.Click
        Me.Close()
    End Sub

    Private Sub mnuEditCopySummary_Click(sender As Object, e As EventArgs) Handles mnuEditCopySummary.Click
        CopySummaryText()
    End Sub

    Private Sub mnuEditCopyAllResults_Click(sender As Object, e As EventArgs) Handles mnuEditCopyAllResults.Click
        CopyAllResults()
    End Sub

    Private Sub mnuEditCopyAllErrors_Click(sender As Object, e As EventArgs) Handles mnuEditCopyAllErrors.Click
        CopyErrorsDataView()
    End Sub

    Private Sub mnuEditCopyAllWarnings_Click(sender As Object, e As EventArgs) Handles mnuEditCopyAllWarnings.Click
        CopyWarningsDataView()
    End Sub

    Private Sub mnuEditFontSizeDecrease_Click(sender As Object, e As EventArgs) Handles mnuEditFontSizeDecrease.Click
        If Me.TextFontSize > 14 Then
            Me.TextFontSize = Me.TextFontSize - 2
        Else
            Me.TextFontSize = Me.TextFontSize - 1
        End If
    End Sub

    Private Sub mnuEditFontSizeIncrease_Click(sender As Object, e As EventArgs) Handles mnuEditFontSizeIncrease.Click
        If Me.TextFontSize >= 14 Then
            Me.TextFontSize = Me.TextFontSize + 2
        Else
            Me.TextFontSize = Me.TextFontSize + 1
        End If
    End Sub

    Private Sub mnuEditResetToDefaults_Click(sender As Object, e As EventArgs) Handles mnuEditResetToDefaults.Click
        ResetOptionsToDefault()
    End Sub

    Private Sub mnuHelpAbout_Click(sender As Object, e As EventArgs) Handles mnuHelpAbout.Click
        ShowAboutBox()
    End Sub

#End Region

#Region "Event Handlers"

    Private Sub mValidateFastaFile_ErrorEvent(strMessage As String) Handles mValidateFastaFile.ErrorEvent
        mValidatorErrorMessage = strMessage
    End Sub

    Private Sub mValidateFastaFile_ProgressChanged(taskDescription As String, percentComplete As Single) Handles mValidateFastaFile.ProgressChanged
        Try
            pbarProgress.Value = CType(percentComplete, Integer)
            Application.DoEvents()
        Catch ex As Exception
            ' Ignore errors here
        End Try
    End Sub
#End Region

End Class