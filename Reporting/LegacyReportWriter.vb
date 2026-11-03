Imports System
Imports System.IO
Imports System.Collections.Generic
Imports GemBox.Spreadsheet

Namespace MachineKarte.Reporting
    ' Runs only worksheet mappings against already-loaded records. No SQL or desktop UI.
    Partial Public Class LegacyReportWriter
        Private ReadOnly imageRoot As String
        Private ReadOnly lookups As IDictionary(Of String, String)
        Private ReadOnly vrNumbers As String()

        Public Sub New(imageRoot As String, lookups As IDictionary(Of String, String), Optional vrNumbers As String() = Nothing)
            Me.imageRoot = Path.GetFullPath(imageRoot)
            Me.lookups = lookups
            Me.vrNumbers = If(vrNumbers, New String() {"", "", ""})
        End Sub

        Private Function ImagePath(relative As String) As String
            Dim target = Path.GetFullPath(Path.Combine(imageRoot, relative.Replace("\", Path.DirectorySeparatorChar)))
            If Not target.StartsWith(imageRoot & Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) Then Throw New ArgumentException("Invalid report image path.")
            If Not File.Exists(target) Then Throw New FileNotFoundException("Original report image is missing.", target)
            Return target
        End Function

        Private Function AddPicture(sheet As ExcelWorksheet, file As String, cell As String, width As Double, height As Double, unit As LengthUnit) As ExcelPicture
            Return sheet.Pictures.Add(file, cell, width, height, unit)
        End Function

        Private Function RetPFImg(value As String) As String
            Return ImagePath(If(value.Trim() = "Passed", "PBox2.png", If(value.Trim() = "Failed", "FBox2.png", "PFNBox2.png")))
        End Function

        Private Function RetABCImg(value As String) As String
            Return ImagePath(If({"A", "B", "C"}.Contains(value.Trim()), value.Trim() & "Box.png", "NBox.png"))
        End Function

        Private Function RetABCImg_2(value As String) As String
            If Not {"A", "B", "C"}.Contains(value.Trim()) Then Throw New ArgumentException("Unknown legacy grade.")
            Return ImagePath(value.Trim() & ".png")
        End Function

        Private Function getProcCodes(id As String, table As String, keyColumn As String, valueColumn As String) As String
            Dim value As String = Nothing
            Return If(lookups.TryGetValue(table.Trim() & "|" & id & "|" & valueColumn, value), value, "")
        End Function

        Private Function getBMWhyInfo(id As String, field As String) As String
            Dim value As String = Nothing
            Return If(lookups.TryGetValue("BMWhy|" & id & "|" & field, value), value, "")
        End Function

        Private Function getMCNamebyID(id As String) As String
            Dim value As String = Nothing
            Return If(lookups.TryGetValue("BMMCUnitWithType|" & id & "|BMMCUnit", value), value, "")
        End Function

        Private Function getBMMCUnit(id As String) As String
            Dim value As String = Nothing
            Return If(lookups.TryGetValue("BMMCUnit|" & id & "|BMMCUnit", value), value, "")
        End Function

        Private Function getPMCategory(id As Integer) As String
            Return getProcCodes(id.ToString(), "PMCategory", "PMCategoryID", "PMCategory")
        End Function

        Private Function getVRNumber(rank As Integer) As String
            Return vrNumbers(rank - 1)
        End Function
    End Class

    Friend NotInheritable Class LegacyRecord
        Private ReadOnly fields As IDictionary(Of String, Object)
        Public Sub New(fields As IDictionary(Of String, Object))
            Me.fields = fields
        End Sub
        Default Public ReadOnly Property Item(name As String) As Object
            Get
                If Not fields.ContainsKey(name) Then Throw New InvalidDataException("Required legacy report field is missing: " & name)
                Return fields(name)
            End Get
        End Property
    End Class

    Friend NotInheritable Class LegacyRows
        Private ReadOnly rows As IList(Of IDictionary(Of String, Object))
        Private position As Integer = -1
        Public Sub New(rows As IList(Of IDictionary(Of String, Object)))
            Me.rows = rows
        End Sub
        Public Function Read() As Boolean
            position += 1
            Return position < rows.Count
        End Function
        Public Sub Close()
        End Sub
        Default Public ReadOnly Property Item(name As String) As Object
            Get
                Dim record As New LegacyRecord(rows(position))
                Return record(name)
            End Get
        End Property
    End Class
End Namespace
