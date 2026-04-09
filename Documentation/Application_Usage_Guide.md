# QuizAPI Application Usage Guide

## Document Information

Application: QuizAPI  
Prepared: March 20, 2026  
Audience: Employees, administrators, and hiring staff using the deployed QuizAPI application

## Overview

QuizAPI is a web-based quiz platform with three main usage areas:

- Employee quiz selection and quiz history tracking
- Administrative quiz management and reporting
- Pre-employment testing for candidates

The application is browser based and is designed to be used through the main site pages rather than through direct API calls for normal day-to-day operation.

## Main Pages

### Index Page

URL: `/`

Purpose:

- Lets employees select one or more quizzes
- Lets employees choose how many random questions to pull
- Supports optional timers
- Supports difficulty and tag filtering
- Provides links to create an account, sign in, and view a profile

### Quiz Page

URL: `/quiz.html`

Purpose:

- Runs the selected quiz
- Submits answers for scoring
- Shows review mode for signed-in employees after submission

### Profile Page

URL: `/profile.html`

Purpose:

- Register a new employee account
- Sign in to an existing employee account
- Edit first and last name
- Change password
- Review tracked quiz history

### Admin Page

URL: `/upload.html`

Purpose:

- Administrative login
- Quiz imports and package uploads
- File review and import processing
- User management
- SMTP configuration
- Pre-employment configuration
- Reporting and export functions

### Pre-Employment Page

URL: `/preemployment.html`

Purpose:

- Candidate-facing test experience
- Uses the saved configuration created by an administrator
- Requires candidate first name and last name
- Sends candidate completion details through the configured email process

### Management Page

URL: `/management.html`

Purpose:

- Secondary management and navigation page used for support and operational access

### API Tools Page

URL: `/api.html`

Purpose:

- Interactive API support page
- Helpful for technical staff, support, and validation

## Employee Workflow

### Create an Account

Employees who want their quiz progress tracked should create an account.

Steps:

1. Open the main page.
2. Select `Create an Account`.
3. Enter first name, last name, email address, and password.
4. Submit the form.
5. After registration, sign in to activate quiz tracking.

Important notes:

- Only signed-in employee quiz attempts are added to quiz history.
- Pre-employment candidates do not use the employee account flow.

### Sign In

Steps:

1. Open the main page or profile page.
2. Select `Sign In`.
3. Enter email address and password.
4. Confirm that the page shows the signed-in state.

When signed in:

- The main page displays the employee identity
- Quiz attempts are saved to the user profile
- Review mode is available after standard quiz submission

### Launch a Quiz

From the main page, employees can configure a custom quiz session.

Available controls:

- One or more quiz selections
- Question amount
- `All questions` option
- Optional timer
- Difficulty filter
- Tag filter

Steps:

1. Open the main page.
2. Select one or more available quizzes.
3. Choose the number of questions or enable `All questions`.
4. Optionally set a timer.
5. Optionally filter by question difficulty or tags.
6. Start the quiz.

### Complete and Review a Quiz

On the quiz page:

- Questions are displayed one at a time or in the configured layout
- The user answers and submits the attempt
- Signed-in employees receive score and review details

After submission, employees can review:

- Score percentage
- Correct and incorrect selections
- Required pass threshold when one is configured

### Resume Partially Completed Progress

For signed-in employees, partially completed quiz progress can be restored.

Guidance:

- If the browser session is interrupted, return to the quiz flow while signed in
- Resume behavior depends on the saved progress associated with the employee account

### View Quiz History

Profile page features include:

- Quiz history table
- Sorting options
- Pagination

History includes:

- Quiz title
- Category
- Score
- Date submitted

## Employee Profile Management

### Edit Profile

Employees can update:

- First name
- Last name

Steps:

1. Sign in.
2. Open the profile page.
3. Use the `Edit Profile` section.
4. Save the changes.

### Change Password

Steps:

1. Sign in.
2. Open the profile page.
3. Use the `Change Password` section.
4. Enter the current password.
5. Enter and confirm the new password.
6. Submit the change.

### Sign Out

Sign out is available from the main page and profile page.

Signing out:

- Removes the active browser token
- Ends quiz tracking until the user signs back in

## Administrative Workflow

### Admin Login

Administrators use the admin page.

Steps:

1. Open `/upload.html`.
2. Enter the administrator email and password.
3. Sign in.
4. Confirm that the admin dashboard sections become visible.

### Upload Quiz Files

Supported upload types:

- `.csv`
- `.txt`
- `.zip`

Common usage:

- CSV and TXT files are used for standard imports
- ZIP packages are used when quiz content includes packaged assets such as images

Steps:

1. Sign in to the admin page.
2. Use the upload form.
3. Select the source file.
4. Upload the file.
5. Review the uploaded file list.
6. Process the upload using the available import action.

### Uploaded Files

The `Uploaded Files` section helps administrators:

- Confirm uploads were received
- Identify uploaded CSV files and packages
- Trigger processing where appropriate

### Quizzes

The `Quizzes` section is the administrative source of truth for imported quiz content.

Administrators can:

- Refresh the list
- Filter by category
- Include archived quizzes
- Copy quiz IDs
- Open the quiz runner
- Archive or restore quizzes
- Permanently delete quizzes when needed

Recommended usage:

- Archive quizzes that should no longer be available to employees
- Delete only when the content should be fully removed and later re-imported from source files

### Import History

The `Import History` section provides a recent record of upload processing.

Use it to:

- Confirm recent import jobs
- Review counts for rows, quizzes, questions, and answers
- Export the history to CSV

### User Management

The `User Management` section allows administrators to:

- Search users
- Create users
- Review names, email addresses, and roles
- Update first name, last name, and role inline

Typical roles:

- Admin
- Employee or user-level roles defined by the application

### SMTP Settings

SMTP is configured from the admin page.

Settings include:

- SMTP host
- Port
- From email
- From name
- Optional notification email
- Username
- Password
- STARTTLS
- SSL

Available actions:

- Save SMTP settings
- Reload SMTP settings
- Send a test email

Recommended practice:

- Verify test email delivery before relying on notification workflows

### Usage Reporting

The `User Metrics` section shows usage reporting.

Reports include:

- Total attempts
- Unique users
- Most taken quizzes
- Average score
- Recent user attempts

Available action:

- Export user activity CSV

### Pre-Employment Quiz Setup

Administrators configure the candidate test from the admin page rather than from the candidate page.

Configuration includes:

- Public page title
- One or more source quizzes
- Random question count pulled from the combined selected pool
- Optional timer
- Pass threshold

Steps:

1. Open the admin page.
2. Go to `Pre-Employment Quiz Setup`.
3. Select one or more source quizzes.
4. Set the question count.
5. Optionally set timer and pass settings.
6. Save the setup.
7. Open the pre-employment page to verify the candidate experience.

### Pre-Employment Attempts

The admin page includes a candidate submissions section.

Administrators can review:

- Candidate first name
- Candidate last name
- Completion date and time
- Configured quiz title
- Source quiz titles
- Score and pass status when stored

Administrative outputs include:

- Printable candidate completion summary

## Pre-Employment Candidate Workflow

The pre-employment page is designed for candidates and should not require admin setup at the time of use.

Steps:

1. Open `/preemployment.html`.
2. Enter first name and last name.
3. Start the test.
4. Complete the questions.
5. Submit the test.

Candidate experience after submission:

- The candidate sees a thank-you message
- Pass or fail is not shown on the public page
- Candidate details are included in the administrative and email workflow

## Quiz Import Expectations

Standard imports rely on source files that include required quiz data columns.

When using ZIP packages:

- Include the source CSV file required for processing
- Include any referenced packaged image assets as needed

If a package is malformed or missing required content, the import process will fail and should be corrected at the source.

## Reporting and Exports

Current exports available from the admin experience include:

- Import history CSV
- User activity CSV

These exports are useful for:

- Operational review
- Usage tracking
- Administrative audits

## Security and Access Notes

- Employee tracking applies to signed-in employee quiz attempts
- Administrative functions should be limited to authorized administrators
- SMTP settings should only be changed by trusted staff
- JWT-based authentication is used for protected operations
- Password fields are masked in the user interface

## Recommended Daily Operating Pattern

For employees:

1. Sign in.
2. Select a quiz set.
3. Complete a quiz.
4. Review results.
5. Check profile history over time.

For administrators:

1. Sign in to the admin page.
2. Review uploads and import history.
3. Confirm quizzes are active and correctly categorized.
4. Review user metrics and candidate completions.
5. Maintain SMTP and pre-employment settings as needed.

## Troubleshooting Tips

### Employee Cannot See Quiz History

Check the following:

- The employee was signed in before launching the quiz
- The quiz submitted successfully
- The profile page is being loaded after the submission completes

### Candidate Page Does Not Match Expected Setup

Check the following:

- The pre-employment setup was saved in the admin page
- The expected source quizzes are selected
- The question count and timer values were saved correctly

### Email Notifications Do Not Arrive

Check the following:

- SMTP host and port
- Authentication settings
- Notification email value
- Test email behavior from the admin page

### Imported Quiz Is Missing

Check the following:

- Upload completed successfully
- Import processing completed successfully
- The quiz is not archived
- Category filters are not hiding the quiz

## Support Reference

Core user-facing pages:

- `/`
- `/quiz.html`
- `/profile.html`
- `/upload.html`
- `/preemployment.html`
- `/management.html`
- `/api.html`

## Revision Note

This guide reflects the current locally deployed application feature set as of March 20, 2026.
