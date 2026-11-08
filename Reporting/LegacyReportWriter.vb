Imports System
Imports System.IO
Imports System.Collections.Generic
Imports System.Linq
Imports GemBox.Spreadsheet
Imports SkiaSharp

Namespace MachineKarte.Reporting
    ' Runs only worksheet mappings against already-loaded records. No SQL or desktop UI.
    Partial Public Class LegacyReportWriter
        Private ReadOnly imageRoot As String
        Private ReadOnly lookups As IDictionary(Of String, String)
        Private ReadOnly vrNumbers As String()
        Private ReadOnly pictureFiles As New Dictionary(Of String, Boolean)(StringComparer.OrdinalIgnoreCase)
        Private ReadOnly pictureSources As New Dictionary(Of String, (Width As Double, Height As Double, Format As ExcelPictureFormat))(StringComparer.OrdinalIgnoreCase)
        Private ReadOnly preparedPictures As New Dictionary(Of (String, Integer, Integer), Byte())

        Public Sub New(imageRoot As String, lookups As IDictionary(Of String, String), Optional vrNumbers As String() = Nothing)
            Me.imageRoot = Path.GetFullPath(imageRoot)
            Me.lookups = lookups
            Me.vrNumbers = If(vrNumbers, New String() {"", "", ""})
        End Sub

        Private Function ImagePath(relative As String) As String
            Dim target = Path.GetFullPath(Path.Combine(imageRoot, relative.Replace("\", Path.DirectorySeparatorChar)))
            If Not target.StartsWith(imageRoot & Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) Then Throw New ArgumentException("Invalid report image path.")
            If Not PictureExists(target) Then Throw New FileNotFoundException("Original report image is missing.", target)
            Return target
        End Function

        Private Function PictureExists(file As String) As Boolean
            Dim exists As Boolean
            If Not pictureFiles.TryGetValue(file, exists) Then
                exists = IO.File.Exists(file)
                pictureFiles(file) = exists
            End If
            Return exists
        End Function

        Private Function AddPicture(sheet As ExcelWorksheet, file As String, cell As String, width As Double, height As Double, unit As LengthUnit) As ExcelPicture
            Return sheet.Pictures.Add(file, cell, width, height, unit)
        End Function

        Private Function AddEvidencePicture(sheet As ExcelWorksheet, file As String, cell As String, width As Double, height As Double, unit As LengthUnit) As ExcelPicture
            Dim maxWidth = LengthUnitConverter.Convert(width, unit, LengthUnit.Point)
            Dim maxHeight = LengthUnitConverter.Convert(height, unit, LengthUnit.Point)
            Dim area = sheet.Cells(cell).MergedRange
            If area IsNot Nothing Then
                maxWidth = Math.Min(maxWidth, Enumerable.Range(area.FirstColumnIndex, area.Width).Sum(Function(column) sheet.Columns(column).GetWidth(LengthUnit.Point)))
                maxHeight = Math.Min(maxHeight, Enumerable.Range(area.FirstRowIndex, area.Height).Sum(Function(row) sheet.Rows(row).GetHeight(LengthUnit.Point)))
            End If
            Dim picture As ExcelPicture = Nothing
            Dim source As (Width As Double, Height As Double, Format As ExcelPictureFormat)
            If Not pictureSources.TryGetValue(file, source) Then
                picture = sheet.Pictures.Add(file, cell)
                source = (picture.Position.Width, picture.Position.Height, picture.PictureFormat)
                pictureSources(file) = source
            End If
            Dim scale = Math.Min(maxWidth / source.Width, maxHeight / source.Height)
            Dim finalWidth = source.Width * scale
            Dim finalHeight = source.Height * scale
            Dim pixelWidth = CInt(Math.Ceiling(finalWidth * 220 / 72))
            Dim pixelHeight = CInt(Math.Ceiling(finalHeight * 220 / 72))
            Dim key = (file, pixelWidth, pixelHeight)
            Dim prepared As Byte() = Nothing
            Dim format = source.Format
            If Not preparedPictures.TryGetValue(key, prepared) Then
                ' Prepare each source/size once, before embedding or serializing repeated photos.
                Using codec = SKCodec.Create(file)
                    ' JPEG output can retain its EXIF orientation after resizing raw pixels.
                    If codec IsNot Nothing AndAlso (codec.EncodedOrigin = SKEncodedOrigin.TopLeft OrElse format = ExcelPictureFormat.Jpeg) AndAlso (codec.Info.Width > pixelWidth OrElse codec.Info.Height > pixelHeight) Then
                        Dim sample = codec.GetScaledDimensions(CSng(Math.Max(pixelWidth / codec.Info.Width, pixelHeight / codec.Info.Height)))
                        Using decoded = SKBitmap.Decode(codec, New SKImageInfo(sample.Width, sample.Height))
                            Using resized = decoded.Resize(New SKImageInfo(Math.Min(pixelWidth, codec.Info.Width), Math.Min(pixelHeight, codec.Info.Height)), New SKSamplingOptions(SKFilterMode.Linear))
                                Using image = SKImage.FromBitmap(resized)
                                    Using data = image.Encode(If(format = ExcelPictureFormat.Jpeg, SKEncodedImageFormat.Jpeg, SKEncodedImageFormat.Png), 90)
                                        prepared = data.ToArray()
                                        If format = ExcelPictureFormat.Jpeg AndAlso codec.EncodedOrigin <> SKEncodedOrigin.TopLeft Then
                                            prepared = WithJpegOrientation(prepared, codec.EncodedOrigin)
                                        End If
                                    End Using
                                End Using
                            End Using
                        End Using
                    End If
                End Using
                preparedPictures(key) = prepared
            End If
            If prepared IsNot Nothing Then
                If picture IsNot Nothing Then sheet.Pictures.Remove(picture)
                ' GemBox retains this managed stream for subsequent saves and workbook clones.
                picture = sheet.Pictures.Add(New MemoryStream(prepared, writable:=False), If(format = ExcelPictureFormat.Jpeg, format, ExcelPictureFormat.Png), cell, finalWidth, finalHeight, LengthUnit.Point)
            ElseIf picture Is Nothing Then
                picture = sheet.Pictures.Add(file, cell, finalWidth, finalHeight, LengthUnit.Point)
            Else
                picture.Position.Width = finalWidth
                picture.Position.Height = finalHeight
            End If
            picture.Position.Mode = PositioningMode.MoveAndSize
            Return picture
        End Function

        Private Shared Function WithJpegOrientation(jpeg As Byte(), origin As SKEncodedOrigin) As Byte()
            ' JPEG APP1 segment: little-endian TIFF with only the original orientation tag.
            Dim exif = Convert.FromHexString("FFE1002245786966000049492A0008000000010012010300010000000000000000000000")
            exif(28) = CByte(origin)
            Dim result(jpeg.Length + exif.Length - 1) As Byte
            Array.Copy(jpeg, result, 2)
            Array.Copy(exif, 0, result, 2, exif.Length)
            Array.Copy(jpeg, 2, result, 2 + exif.Length, jpeg.Length - 2)
            Return result
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
