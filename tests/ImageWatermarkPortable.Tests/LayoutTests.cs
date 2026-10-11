using System.Drawing;
using ImageWatermarkPortable.Forms;

namespace ImageWatermarkPortable.Tests;

public sealed class LayoutTests
{
    [Fact]
    public void Top_Level_Controls_Do_Not_Overlap()
    {
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                using var form = new MainForm();
                form.Show();
                Application.DoEvents();
                Thread.Sleep(150);

                var controls = form.Controls.Cast<Control>().Where(c => c.Visible).ToList();

                for (var i = 0; i < controls.Count; i++)
                {
                    for (var j = i + 1; j < controls.Count; j++)
                    {
                        var a = controls[i];
                        var b = controls[j];
                        var overlap = Rectangle.Intersect(a.Bounds, b.Bounds);
                        if (overlap.Width > 1 && overlap.Height > 1)
                        {
                            throw new InvalidOperationException(
                                $"{a.GetType().Name} {a.Bounds} overlaps {b.GetType().Name} {b.Bounds}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(failure);
    }

    [Fact]
    public void Required_Input_Controls_Exist_On_Form()
    {
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                using var form = new MainForm();
                form.Show();
                Application.DoEvents();

                var textBoxes = form.Controls.OfType<TextBox>().ToList();
                if (textBoxes.Count != 3)
                {
                    throw new InvalidOperationException($"expected 3 TextBox (source/destination/watermark), found {textBoxes.Count}");
                }
                if (textBoxes.Any(t => !t.Enabled || t.ReadOnly))
                {
                    throw new InvalidOperationException("an input TextBox is disabled or read-only");
                }

                if (form.Controls.OfType<TrackBar>().Count() != 2)
                {
                    throw new InvalidOperationException($"expected 2 TrackBar, found {form.Controls.OfType<TrackBar>().Count()}");
                }

                if (form.Controls.OfType<Button>().Count(b => b.Text == "瀏覽...") != 2)
                {
                    throw new InvalidOperationException("two browse buttons expected");
                }

                if (!form.Controls.OfType<Button>().Any(b => b.Text.StartsWith('#')))
                {
                    throw new InvalidOperationException("color picker button missing");
                }

                if (!form.Controls.OfType<Button>().Any(b => b.Text == "開始處理"))
                {
                    throw new InvalidOperationException("start button missing");
                }

                if (!form.Controls.OfType<GroupBox>().Any())
                {
                    throw new InvalidOperationException("preview group box missing");
                }

                if (!form.Controls.OfType<ProgressBar>().Any())
                {
                    throw new InvalidOperationException("progress bar missing");
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(failure);
    }
}
