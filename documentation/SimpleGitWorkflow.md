# Setting Up *Without* a Fork with Visual Studio 2015

 * See also [Setting up a Repo in VS 2015](SettingUpRepoInVS2015.md) for important background material. 
 * See also [Open Source GitHub Setup and Workflow](OpenSourceGitWorkflow.md) for the setup needed for pull requests.  

Here we describe how to use Visual Studio 2015 to set a local build of the GitHub project https://github.com/Microsoft/perfview.   
Because this project is open source, anyone can read it so this works even if you don't have a GitHub account, but
of course you will only have read-only access.   

  1. Open Visual Studio and select the View -> Team Explorer menu item
  2. This brings up the Team Explorer pane. At the top of the display is a bold word (probably Home) which
     indicates which Team Explorer view is being displayed. If you left click
     on this bolded item you will get a menu of possible displays. Select Projects -> Manage Connections
  3. At the bottom of this display you should see a section called 'Local GIT Repositories' and under this a
     set of hyperlinks. Click on the 'Clone' hyperlink.
  4. It will bring up a dialog box where you enter two pieces of information
     a. The name of the source to clone.  Enter the GitHub URL (in our example https://github.com/Microsoft/perfview)
     b. The name of the local directory to place the clone (The default is probably OK, but you can decide to place it somewhere else)
  5. Click the Clone button. 
  
At this point Visual Studio will create a local clone of all the files in a project in the directory you specified. This represents
the final state of all files in the default branch (probably 'main'). It also clones the repository (thus the history for all time)
and places it in a hidden .git directory in the directory you specified. It is this cloned local repository where most GIT 
commands you enact will operate. By default your clone ended up in %HOMEPATH%\Source\Repos\PerfView.   

Once the clone is complete, you will see the new local repository added to the list under 'Local Repositories'.
You can see which one is 'active' because it is bold-faced. This is the repository that all other VS GIT commands will
operate on. Double click on the new entry (perfView in this example) to make it the active GIT repo.

This will bring you to the 'Home' display for a local GIT repository. You will see buttons for 'changes', 'branches', 'sync', etc. 
At the bottom you will see a list of .SLN files. There are all of the Visual Studio solution files that were found in the clone
of the repository. This makes it convenient to open VS to do a build. In the case of PerfView, you want the PerfView.sln file.   
After opening the solution file you can start building (Build -> Build Solution or Ctrl-Shift-B).

## Making changes

GIT is a bit unusual in that there is no 'checkout' command. You can modify anything in the local copy of the files 
at will. You can think of it as modifying a file that will cause an automatic checkout, but this works no matter how the file
was changed (thus it is more like every git command that starts by looking at files to see what the checked out set is in the repo).

## Committing Trivial Changes to Master Branch

As mentioned, GIT has the concept of the 'active branch' which is the branch the 'commit' operation uses to stamp the 
commit (snapshot) with the predecessor commit (snapshot).  Thus the active branch is what determines the 'history' of 
a commit and is very important, you want this to be accurate. After setting up a local repository, the active Branch
is almost certainly one called 'main' (but you can check by going to the View -> Team Explorer -> Branches).

Master represents the 'latest', 'default' version of the code in your repository. Thus after making updates to 
particular files, simply commit them to main.  

When you have a set of files you wish to commit (check in), you can either right click on the solution in the 'Solution Explorer' 
and select 'Commit', or use the View -> Team Explorer menu item to get the Team explorer pane and left click on the top
bolded item and select 'Changes'. This will bring up the 'Changes' view of the Team Explorer pane that lets you commit. This
pane will show you all the files that have changed, (you can right click on each and select Compare with Unmodified to diff).  
There is also a text box at the top in which you should put a message.   

By convention, you should begin this comment with ONE LINE SUMMARIZES THE CHANGE AS A WHOLE.   This one line is what shows up in 'short'
descriptions of the commit.  After that line, you can write a longer explanation if desired. 

At the top of the pane is the active branch.  It is likely to say 'main', but whatever it is, this is the branch that
you are committing to.  

After you have filled out the description, you can simply hit the 'Commit All' button to commit your changes.  

## Don't use Master Branch for Non-trivial Features.  

Committing directly to the main branch is fine for small, independent edits, however it is 
a *bad idea for any feature that **might** involve multiple commits*.   This includes any pull 
request because most pull requests are likely to have commits to respond to maintainer 
feedback.  (See [Open Source GitHub Setup and Workflow](OpenSourceGitWorkflow.md) 
for more on pull requests).   Instead you should give commits that are the first of a multi-part 
edit their own branch.

By giving the multi-commit edit its own branch, the commits can be kept together, and this makes for significantly more 
agile history (you can take or leave this feature more easily). It also makes the history significantly easier to read.

This is not hard to do, because you don't need to make the choice of whether to do the commit on
the main branch directly, or creating its own branch until you actually are trying to commit. 
It is only at commit time that you need to make the choice.

To make a new branch for a feature, follow the same procedure as committing to main, but before
hitting the 'Commit All' button, click the hyperlink on the branch name at the top of the pane.  

This takes you to the Branches pane, and the current branch (likely to be main) is highlighted.
You can then right click on that branch and select 'New Local Branch From' which will prompt
you for a new branch name and hit the 'Create Branch' button.   This new branch is now the
active branch so you can go back to the 'Changes' pane (left click on the 'Branches' Header) and
complete the commit.

## Switching Branches 

Once you have more than one branch, you will want to be able to switch the active branch from 
one branch to another.   You do this in the 'Branches' pane (View -> Team Explorer -> Branches).   
Note that whatever branch is bolded is the active branch and simply double clicking on a 
branch makes it active.   

When you switch branches YOU UPDATE THE LOCAL REPOSITORY WITH THE CONTENTS OF THAT BRANCH.
Thus with one double click potentially all the local files in the repo may change. If you have any uncommitted
changes, THE SWITCH IS LIKELY TO FAIL (it checks for this before doing any updates).
Typically you only switch branches if you have no modified files (in which case the switch is guaranteed to succeed).
If you hit this error (and you will), the solution is to commit your changes (if necessary to a
new temporary branch for that purpose) at which point you can switch branches.

## Synchronizing the GitHub repository and the Local Repository.  

After you have made edits, or when you want to pull down any GitHub changes to your local repository
you need to sync.   Technically syncing is actually a combination of two operations a
push (updating the GitHub repository with what you have changed), and a pull (updating your local repository
with what is new from the GitHub repo).  

One important point is that both a push and a pull operation operate on a branch (they cause the 
branch to update) and both might cause conflicts (if a file has been changed in both places). Generally
speaking you should only push or pull on your local 'main' branch. Otherwise all the bad effects
of interleaving unrelated changes discussed previously come into play. If you think you need to sync
some other branch, it is more likely that you need to synchronize main, and then rebase your other 
branch on top of main  (TODO reference instructions for this).  

To synchronize the main branch do the following

  1. Set the active branch to 'main' with no modified files, by following the procedure 'Switching Branches'
     above. You may need to commit things to a temporary local branch to get to this state, but 
     one way or the other you need to get there.
  2. Go the Synchronization pane of team explorer.  
  3. Click on the 'sync' hyperlink.

If there are no conflicts doing the update, then the resulting updates are automatically committed to 
the active branch.   

If there are merge conflicts Visual Studio will stop and give you an opportunity to fix them.

## Discarding Changes

If you decide you want to abandon changes, simply go to the View -> Team Explorer -> Changes view, select all
the files, right click and select Undo Changes.  

## Viewing history

From the View -> Team Explorer -> Branches view you can right click on any branch (it does not need to
be the active one) and select 'View History'.   

## Review

At this point we have described the critical workflows 
  1. Synchronization with what is in GitHub
  2. Making changes 
  3. Committing (checking in)




