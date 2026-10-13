using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MySql.Data.MySqlClient;

namespace Secucode
{
    public partial class TeacherExamFormWindow : Window
    {
        private const int MinQuestionSlots = 10;

        private readonly int teacherUserId;
        private readonly Test? test;
        private readonly List<TextBox> questionBoxes = new();

        public TeacherExamFormWindow(int teacherUserId, Test? test = null)
        {
            InitializeComponent();
            this.teacherUserId = teacherUserId;
            this.test = test;

            PopulateComboBox(cbBranch, "SELECT id, branch_name AS name FROM branches ORDER BY branch_name");
            PopulateComboBox(cbClass, "SELECT id, class_name AS name FROM classes ORDER BY class_name");
            PopulateComboBox(cbBatch, "SELECT id, batch_name AS name FROM batches ORDER BY batch_name");

            if (test != null)
            {
                txtExamName.Text = test.Name;
                dpExamDate.SelectedDate = test.ExamDate;
                txtTimeLimit.Text = test.TimeLimit.ToString();
                cbBranch.SelectedValue = test.BranchId;
                cbClass.SelectedValue = test.ClassId;
                cbBatch.SelectedValue = test.BatchId;
            }

            // Existing questions plus a few blank slots; blank ones are ignored on save.
            var existing = test?.Questions ?? new List<string>();
            int slots = Math.Max(MinQuestionSlots, existing.Count + 3);
            for (int i = 0; i < slots; i++)
                AddQuestionBox(i + 1, i < existing.Count ? existing[i] : "");
        }

        private void AddQuestionBox(int number, string text)
        {
            var box = new TextBox { Text = text, FontSize = 16, Padding = new Thickness(6), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
            var label = new TextBlock { Text = $"Q{number}", FontSize = 16, FontWeight = FontWeights.Bold, Width = 40, VerticalAlignment = VerticalAlignment.Center };
            var row = new DockPanel { Margin = new Thickness(5) };
            DockPanel.SetDock(label, Dock.Left);
            row.Children.Add(label);
            row.Children.Add(box);
            questionsList.Items.Add(row);
            questionBoxes.Add(box);
        }

        private static void PopulateComboBox(ComboBox comboBox, string query)
        {
            try
            {
                using MySqlConnection conn = Db.Open();
                using MySqlCommand cmd = new MySqlCommand(query, conn);
                using MySqlDataReader reader = cmd.ExecuteReader();

                var items = new List<KeyValuePair<int, string>>();
                while (reader.Read())
                    items.Add(new(reader.GetInt32("id"), reader.GetString("name")));

                comboBox.ItemsSource = items;
                comboBox.DisplayMemberPath = "Value";
                comboBox.SelectedValuePath = "Key";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error loading combo box data: " + ex.Message);
            }
        }

        private void btnSubmit_Click(object sender, RoutedEventArgs e)
        {
            string examName = txtExamName.Text.Trim();
            DateTime? selectedDate = dpExamDate.SelectedDate;

            if (string.IsNullOrEmpty(examName))
            {
                MessageBox.Show("Exam name is required.");
                return;
            }
            if (!selectedDate.HasValue)
            {
                MessageBox.Show("Please select a valid exam date.");
                return;
            }
            if (!int.TryParse(txtTimeLimit.Text.Trim(), out int timeLimit) || timeLimit <= 0)
            {
                MessageBox.Show("Please enter a valid positive time limit (minutes).");
                return;
            }
            if (cbBranch.SelectedValue == null || cbClass.SelectedValue == null || cbBatch.SelectedValue == null)
            {
                MessageBox.Show("Branch, Class, and Batch must all be selected.");
                return;
            }

            List<string> questions = questionBoxes.Select(b => b.Text.Trim()).Where(q => q.Length > 0).ToList();
            if (questions.Count == 0)
            {
                MessageBox.Show("Please add at least one question.");
                return;
            }

            try
            {
                using MySqlConnection conn = Db.Open();
                using MySqlTransaction tx = conn.BeginTransaction();

                string sql = test != null
                    ? @"UPDATE examinations
                        SET name=@name, date=@date, time_limit=@time_limit,
                            branch_id=@branchId, class_id=@classId, batch_id=@batchId
                        WHERE id=@id"
                    : @"INSERT INTO examinations (name, date, time_limit, branch_id, class_id, batch_id, created_by)
                        VALUES (@name, @date, @time_limit, @branchId, @classId, @batchId, @created_by)";

                long examId;
                using (MySqlCommand cmd = new MySqlCommand(sql, conn, tx))
                {
                    cmd.Parameters.AddWithValue("@name", examName);
                    cmd.Parameters.AddWithValue("@date", selectedDate.Value.Date);
                    cmd.Parameters.AddWithValue("@time_limit", timeLimit);
                    cmd.Parameters.AddWithValue("@branchId", cbBranch.SelectedValue);
                    cmd.Parameters.AddWithValue("@classId", cbClass.SelectedValue);
                    cmd.Parameters.AddWithValue("@batchId", cbBatch.SelectedValue);
                    cmd.Parameters.AddWithValue("@id", test?.Id ?? 0);
                    cmd.Parameters.AddWithValue("@created_by", teacherUserId);
                    cmd.ExecuteNonQuery();
                    examId = test?.Id ?? cmd.LastInsertedId;
                }

                // Questions have no dependents (submissions store the text), so replace them wholesale.
                using (MySqlCommand del = new MySqlCommand("DELETE FROM questions WHERE examination_id=@id", conn, tx))
                {
                    del.Parameters.AddWithValue("@id", examId);
                    del.ExecuteNonQuery();
                }
                foreach (string q in questions)
                {
                    using MySqlCommand ins = new MySqlCommand("INSERT INTO questions (examination_id, question_text) VALUES (@id, @text)", conn, tx);
                    ins.Parameters.AddWithValue("@id", examId);
                    ins.Parameters.AddWithValue("@text", q);
                    ins.ExecuteNonQuery();
                }

                tx.Commit();
                MessageBox.Show("Examination and questions saved successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
