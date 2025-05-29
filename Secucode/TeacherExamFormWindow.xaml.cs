using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using MySql.Data.MySqlClient;
using System.Windows.Media;

namespace Secucode
{
    public partial class TeacherExamFormWindow : Window
    {
        private readonly string teacherUsername;
        private readonly Test test;
        private readonly string connectionString = "Server=localhost;Database=secucode;User Id=root;Password=root;";
        private List<Question> existingQuestions = new List<Question>(); // To track existing questions

        public TeacherExamFormWindow(string username, Test test = null)
        {
            InitializeComponent();
            teacherUsername = username ?? throw new ArgumentNullException(nameof(username));
            this.test = test;

            // Populate branch, class, and batch combo boxes from the database
            PopulateComboBoxes();

            if (test != null)
            {
                LoadTestDetails(test);  // Populate the test details if updating
            }
            else
            {
                InitializeQuestionsList();  // Initialize new questions for a new test
            }
        }

        // Populate branch, class, and batch dropdowns from the database
        private void PopulateComboBoxes()
        {
            PopulateComboBox(cbBranch, "SELECT id, branch_name FROM branches", "branch_name", "id");
            PopulateComboBox(cbClass, "SELECT id, class_name FROM classes", "class_name", "id");
            PopulateComboBox(cbBatch, "SELECT id, batch_name FROM batches", "batch_name", "id");
        }

        private void PopulateComboBox(ComboBox comboBox, string query, string displayMember, string valueMember)
        {
            try
            {
                using (MySqlConnection conn = new MySqlConnection(connectionString))
                {
                    conn.Open();
                    MySqlCommand cmd = new MySqlCommand(query, conn);
                    MySqlDataReader reader = cmd.ExecuteReader();

                    var items = new List<dynamic>();
                    while (reader.Read())
                    {
                        items.Add(new { Display = reader[displayMember].ToString(), Value = reader[valueMember] });
                    }

                    comboBox.ItemsSource = items;
                    comboBox.DisplayMemberPath = "Display";  // Sets the visible name in the dropdown
                    comboBox.SelectedValuePath = "Value";    // Sets the underlying value (id) that gets selected
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error loading combo box data: " + ex.Message);
            }
        }

        // Load the existing test details if editing
        private void LoadTestDetails(Test test)
        {
            txtExamName.Text = test.Name;
            if (!string.IsNullOrEmpty(test.Date))
            {
                dpExamDate.SelectedDate = DateTime.Parse(test.Date);
            }
            else
            {
                dpExamDate.SelectedDate = null; // or some default date handling
            }
            txtTimeLimit.Text = test.TimeLimit.ToString();

            // Load the existing questions from the database
            LoadExistingQuestions(test.Id);

            // Set the branch, class, and batch selections based on their IDs
            cbBranch.SelectedValue = test.BranchId;
            cbClass.SelectedValue = test.ClassId;
            cbBatch.SelectedValue = test.BatchId;
        }

        // Load existing questions
        private void LoadExistingQuestions(int testId)
        {
            using (MySqlConnection conn = new MySqlConnection(connectionString))
            {
                conn.Open();
                string query = "SELECT id, question_text FROM questions WHERE examination_id = @examId";
                using (MySqlCommand cmd = new MySqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@examId", testId);
                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            existingQuestions.Add(new Question
                            {
                                Id = reader.GetInt32("id"),
                                Text = reader.GetString("question_text")
                            });
                        }
                    }
                }
            }

            // Dynamically add TextBoxes for each question to the UI
            questionsList.Items.Clear();
            foreach (var question in existingQuestions)
            {
                TextBox textBox = new TextBox
                {
                    Text = question.Text,
                    Margin = new Thickness(5)
                };
                questionsList.Items.Add(textBox);
            }
        }

        // Initialize default questions for a new test
        private void InitializeQuestionsList()
        {
            questionsList.Items.Clear();
            for (int i = 1; i <= 10; i++)
            {
                TextBox textBox = new TextBox
                {
                    Text = $"Question {i}:",
                    Margin = new Thickness(5)
                };
                questionsList.Items.Add(textBox);
            }
        }

        // Submit button click event
        private void btnSubmit_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string examName = txtExamName.Text.Trim();
                DateTime? selectedDate = dpExamDate.SelectedDate;
                string timeLimitText = txtTimeLimit.Text.Trim();

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

                if (!int.TryParse(timeLimitText, out int timeLimit) || timeLimit <= 0)
                {
                    MessageBox.Show("Please enter a valid positive time limit.");
                    return;
                }

                if (cbBranch.SelectedValue == null || cbClass.SelectedValue == null || cbBatch.SelectedValue == null)
                {
                    MessageBox.Show("Branch, Class, and Batch must all be selected.");
                    return;
                }

                int branchId = Convert.ToInt32(cbBranch.SelectedValue);
                int classId = Convert.ToInt32(cbClass.SelectedValue);
                int batchId = Convert.ToInt32(cbBatch.SelectedValue);

                List<string> updatedQuestions = new List<string>();
                foreach (var item in questionsList.Items)
                {
                    if (item is TextBox textBox)
                    {
                        string question = textBox.Text.Trim();
                        if (!string.IsNullOrEmpty(question))
                            updatedQuestions.Add(question);
                    }
                }

                if (updatedQuestions.Count == 0)
                {
                    MessageBox.Show("Please add at least one question.");
                    return;
                }

                using (MySqlConnection conn = new MySqlConnection(connectionString))
                {
                    conn.Open();

                    if (test != null)
                    {
                        // Update logic
                        string updateQuery = @"UPDATE examinations 
                                       SET name=@name, date=@date, time_limit=@time_limit, 
                                           branch_id=@branchId, class_id=@classId, batch_id=@batchId 
                                       WHERE id=@id";
                        using (MySqlCommand cmd = new MySqlCommand(updateQuery, conn))
                        {
                            cmd.Parameters.AddWithValue("@name", examName);
                            cmd.Parameters.AddWithValue("@date", selectedDate.Value);
                            cmd.Parameters.AddWithValue("@time_limit", timeLimit);
                            cmd.Parameters.AddWithValue("@branchId", branchId);
                            cmd.Parameters.AddWithValue("@classId", classId);
                            cmd.Parameters.AddWithValue("@batchId", batchId);
                            cmd.Parameters.AddWithValue("@id", test.Id);

                            cmd.ExecuteNonQuery();
                        }

                        UpdateQuestions(conn, test.Id, updatedQuestions);
                    }
                    else
                    {
                        // Insert new exam
                        string insertQuery = @"INSERT INTO examinations 
                                       (name, date, time_limit, branch_id, class_id, batch_id, created_by) 
                                       VALUES 
                                       (@name, @date, @time_limit, @branchId, @classId, @batchId, @created_by)";
                        using (MySqlCommand cmd = new MySqlCommand(insertQuery, conn))
                        {
                            cmd.Parameters.AddWithValue("@name", examName);
                            cmd.Parameters.AddWithValue("@date", selectedDate.Value);
                            cmd.Parameters.AddWithValue("@time_limit", timeLimit);
                            cmd.Parameters.AddWithValue("@branchId", branchId);
                            cmd.Parameters.AddWithValue("@classId", classId);
                            cmd.Parameters.AddWithValue("@batchId", batchId);
                            cmd.Parameters.AddWithValue("@created_by", teacherUsername);

                            cmd.ExecuteNonQuery();
                            long examId = cmd.LastInsertedId;

                            InsertQuestions(conn, examId, updatedQuestions);
                        }
                    }

                    MessageBox.Show("Examination and questions saved successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    Close();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Insert questions into the database
        private void InsertQuestions(MySqlConnection conn, long examId, List<string> questions)
        {
            string questionQuery = "INSERT INTO questions (examination_id, question_text) VALUES (@examination_id, @question_text)";
            foreach (string question in questions)
            {
                using (MySqlCommand questionCmd = new MySqlCommand(questionQuery, conn))
                {
                    questionCmd.Parameters.AddWithValue("@examination_id", examId);
                    questionCmd.Parameters.AddWithValue("@question_text", question);
                    questionCmd.ExecuteNonQuery();
                }
            }
        }

        // Update questions (existing questions are updated, new ones added, others remain unchanged)
        private void UpdateQuestions(MySqlConnection conn, long examId, List<string> updatedQuestions)
        {
            // Fetch the existing questions from the database
            List<Question> existingQuestions = GetExistingQuestions(conn, examId);

            // Update existing questions and insert new ones
            for (int i = 0; i < updatedQuestions.Count; i++)
            {
                if (i < existingQuestions.Count)
                {
                    // Update existing question
                    string updateQuery = "UPDATE questions SET question_text = @question_text WHERE id = @id";
                    using (MySqlCommand cmd = new MySqlCommand(updateQuery, conn))
                    {
                        cmd.Parameters.AddWithValue("@question_text", updatedQuestions[i]);
                        cmd.Parameters.AddWithValue("@id", existingQuestions[i].Id);
                        cmd.ExecuteNonQuery();
                    }
                }
                else
                {
                    // Insert new question if there are more questions than existing ones
                    InsertQuestions(conn, examId, new List<string> { updatedQuestions[i] });
                }
            }

            // If there are fewer questions now, delete the extra ones from the database
            if (existingQuestions.Count > updatedQuestions.Count)
            {
                for (int i = updatedQuestions.Count; i < existingQuestions.Count; i++)
                {
                    string deleteQuery = "DELETE FROM questions WHERE id = @id";
                    using (MySqlCommand cmd = new MySqlCommand(deleteQuery, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", existingQuestions[i].Id);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
        }

        // Fetch existing questions from the database
        private List<Question> GetExistingQuestions(MySqlConnection conn, long examId)
        {
            List<Question> questions = new List<Question>();

            string selectQuery = "SELECT id, question_text FROM questions WHERE examination_id = @examination_id";
            using (MySqlCommand cmd = new MySqlCommand(selectQuery, conn))
            {
                cmd.Parameters.AddWithValue("@examination_id", examId);
                using (MySqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        questions.Add(new Question
                        {
                            Id = reader.GetInt32("id"),
                            Text = reader.GetString("question_text")
                        });
                    }
                }
            }

            return questions;
        }

        // Cancel button click event
        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        // Helper method to find child element in the visual tree
        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child != null && child is T)
                {
                    return (T)child;
                }
                else
                {
                    T childOfChild = FindVisualChild<T>(child);
                    if (childOfChild != null)
                    {
                        return childOfChild;
                    }
                }
            }
            return null;
        }
    }

    
}
